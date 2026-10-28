using System.Text.Json;

namespace HireZero.Integrations.Codex;

// The pinned app-server's item/completed snapshot is authoritative. Progress is not the main course.
internal sealed class CodexTextOutput(int characterLimit)
{
    private sealed class Message
    {
        public string? Phase;
        public string Text = "";
        public long StreamedCharacters;
        public long ObservedCharacters;
        public bool Completed;
    }

    private readonly Dictionary<string, Message> _messages = new(StringComparer.Ordinal);
    private readonly List<Message> _ordered = [];
    private long _observedCharacters;

    public void ObserveItem(JsonElement item, bool completed)
    {
        var message = Get(Required(item, "id"));
        var phase =
            item.TryGetProperty("phase", out var value) && value.ValueKind != JsonValueKind.Null
                ? value.GetString()
                : null;
        if (phase is not (null or "commentary" or "partial_answer" or "final_answer"))
            throw new ModelTransportException("Unknown assistant message phase; output withheld.");
        message.Phase = phase ?? message.Phase;
        var text = Required(item, "text");
        ObserveLength(message, text.Length);
        if (completed)
        {
            message.Text = text;
            message.Completed = true;
        }
    }

    public void ObserveDelta(JsonElement delta)
    {
        var message = Get(Required(delta, "itemId"));
        message.StreamedCharacters += Required(delta, "delta").Length;
        ObserveLength(message, message.StreamedCharacters);
    }

    public string Complete()
    {
        if (_ordered.Any(message => !message.Completed))
            throw new ModelTransportException("Assistant message completion was not confirmed; output withheld.");
        // Partial answers are stable answer text. A missing phase remains compatible with legacy models.
        var body = string.Join(
            "\n\n",
            _ordered
                .Where(message => message.Phase != "commentary" && !string.IsNullOrWhiteSpace(message.Text))
                .Select(message => message.Text)
        );
        if (string.IsNullOrWhiteSpace(body))
            throw new ModelTransportException("A completed assistant answer was not confirmed; output withheld.");
        CheckLength(body.Length);
        return body;
    }

    private Message Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ModelTransportException("Assistant message identity is missing; output withheld.");
        if (!_messages.TryGetValue(id, out var message))
        {
            message = new Message();
            _messages.Add(id, message);
            _ordered.Add(message);
        }
        if (message.Completed)
            throw new ModelTransportException("An already completed assistant message changed; output withheld.");
        return message;
    }

    private void ObserveLength(Message message, long length)
    {
        // Count each item's high-water mark, including commentary, without billing snapshots twice.
        var observed = Math.Max(message.ObservedCharacters, length);
        _observedCharacters += observed - message.ObservedCharacters;
        message.ObservedCharacters = observed;
        CheckLength(_observedCharacters);
    }

    private void CheckLength(long length)
    {
        if (length > characterLimit)
            throw new ModelTransportException("Local output character allowance exceeded; native work stopped.");
    }

    private static string Required(JsonElement item, string key) =>
        item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new ModelTransportException("Assistant message data is incomplete; output withheld.");
}
