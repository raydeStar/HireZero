namespace HireZero.Core;

public sealed record BrowserLink(string Text, string Url);

public sealed record BrowserScreenshot(string MediaType, string Base64);

public sealed record BrowserCapture(
    int Step,
    string Url,
    DateTimeOffset CapturedAt,
    string Title,
    string Text,
    bool Truncated,
    BrowserLink[] Links,
    BrowserScreenshot? Screenshot = null
);

public sealed record BrowserRefusal(string Url, string Reason);

public sealed record BrowserDriverNetwork(int Attempts, long ReceivedBytes);

// A driver receipt is evidence, not authority or proof that its operating-system sandbox was released.
public sealed record BrowserResult(
    string JobId,
    string State,
    int CompletedSteps,
    bool Partial,
    BrowserCapture[] Captures,
    BrowserRefusal[] Blocked,
    string[] Warnings,
    bool ContextClosed,
    BrowserDriverNetwork Network,
    long DurationMs,
    string? Error = null
)
{
    private static bool Bounded(string? value, int length) => value is not null && value.Length <= length;

    public void Validate(BrowserRequest request)
    {
        request.Validate();
        if (
            JobId != request.JobId
            || State is not ("completed" or "failed" or "timed-out" or "canceled")
            || CompletedSteps < 0
            || CompletedSteps > request.Steps.Length
            || State == "completed" && CompletedSteps != request.Steps.Length
            || Captures is null
            || Captures.Length > 4
            || Blocked is null
            || Blocked.Length > 20
            || Warnings is null
            || Warnings.Length > 12
            || Network is null
            || Network.Attempts < 0
            || Network.Attempts > request.Limits.Requests + 1
            || Network.ReceivedBytes < 0
            || Network.ReceivedBytes > request.Limits.Bytes
            || DurationMs < 0
            || DurationMs > (request.Limits.Seconds + 10L) * 1000
            || Error is not null && Error.Length > 500
            || Blocked.Any(b => b is null || !Bounded(b.Url, 2000) || !Bounded(b.Reason, 300))
            || Warnings.Any(w => !Bounded(w, 300))
        )
            throw new WorkspaceException("The browser result does not match its approved job or output limits.", 422);

        var origins = request
            .Origins.Select(BrowserRequest.Address)
            .Select(BrowserRequest.Origin)
            .ToHashSet(StringComparer.Ordinal);
        var previousStep = 0;
        foreach (var capture in Captures)
        {
            if (
                capture is null
                || capture.Step <= previousStep
                || capture.Step > CompletedSteps
                || request.Steps[capture.Step - 1].Action != "read"
                || !origins.Contains(BrowserRequest.Origin(BrowserRequest.Address(capture.Url)))
                || capture.CapturedAt == default
                || capture.CapturedAt > DateTimeOffset.UtcNow.AddMinutes(1)
                || !Bounded(capture.Title, 200)
                || !Bounded(capture.Text, 8000)
                || capture.Links is null
                || capture.Links.Length > 40
                || capture.Links.Any(link => link is null || !Bounded(link.Text, 160) || !Bounded(link.Url, 2000))
            )
                throw new WorkspaceException("A browser capture leaves its approved scope or reading step.", 422);
            previousStep = capture.Step;
            if (capture.Screenshot is { } screenshot)
            {
                if (
                    !request.Screenshots
                    || screenshot.MediaType != "image/jpeg"
                    || screenshot.Base64 is null
                    || screenshot.Base64.Length > 349528
                )
                    throw new WorkspaceException("A browser screenshot exceeds its approved format or size.", 422);
                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(screenshot.Base64);
                }
                catch (FormatException)
                {
                    throw new WorkspaceException("A browser screenshot is not valid base64.", 422);
                }
                if (
                    bytes.Length is < 5 or > 256 * 1024
                    || bytes[0] != 0xff
                    || bytes[1] != 0xd8
                    || bytes[2] != 0xff
                    || bytes[^2] != 0xff
                    || bytes[^1] != 0xd9
                )
                    throw new WorkspaceException("A browser screenshot is not a bounded JPEG envelope.", 422);
            }
        }
        if (
            State == "completed" && Captures.Length != request.Steps.Count(step => step.Action == "read")
            || !Partial && (Blocked.Length > 0 || Warnings.Length > 0 || Captures.Any(capture => capture.Truncated))
        )
            throw new WorkspaceException("The browser receipt hides missing or partial evidence.", 422);
    }
}
