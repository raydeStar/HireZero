using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HireZero.Integrations.OpenClaw;

// Memory in the owner's OpenClaw cell: notes are Markdown files in its workspace (memory/*.md, MEMORY.md),
// indexed for keyword search by memory-core and consolidated overnight by its dreaming sweep.
public sealed class OpenClawMemory(OpenClawCells cells) : IMemoryStore
{
    private const string Cell = IModelGateway.OwnerCell;
    private static readonly Regex Day = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.None, TimeSpan.FromMilliseconds(50));
    private static readonly Regex Topic = new(@"^[a-z]{1,24}$", RegexOptions.None, TimeSpan.FromMilliseconds(50));
    private readonly SemaphoreSlim _writes = new(1, 1);
    private int _indexing,
        _indexAgain;

    private string Workspace => cells.Workspace(Cell);
    private string NotesFolder
    {
        get
        {
            var folder = Path.Combine(Workspace, "memory");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public async Task<MemoryHit[]> SearchAsync(string query, int max, CancellationToken token)
    {
        var output = await cells.CliAsync(
            Cell,
            [
                "memory",
                "search",
                "--query",
                query,
                "--max-results",
                max.ToString(CultureInfo.InvariantCulture),
                "--json",
            ],
            TimeSpan.FromSeconds(60),
            token
        );
        return Parse(output);
    }

    public static MemoryHit[] Parse(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            if (
                !document.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array
            )
                return [];
            return
            [
                .. results
                    .EnumerateArray()
                    .Select(result => new MemoryHit(
                        Text(result, "path") ?? "",
                        result.TryGetProperty("startLine", out var line) && line.TryGetInt32(out var number)
                            ? number
                            : 1,
                        result.TryGetProperty("score", out var score) && score.TryGetDouble(out var value)
                            ? Math.Round(value, 3)
                            : 0,
                        Text(result, "snippet") ?? "",
                        Text(result, "source") ?? "memory",
                        []
                    )),
            ];
        }
        catch (JsonException)
        {
            throw new ModelUnavailableException("OpenClaw memory search returned an unreadable answer.");
        }
    }

    public async Task WriteNoteAsync(string day, string topic, string markdown, CancellationToken token)
    {
        if (!Day.IsMatch(day) || !Topic.IsMatch(topic))
            throw new WorkspaceException("That note name isn't allowed.", 400);
        await _writes.WaitAsync(token);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(NotesFolder, day + "-hirezero-" + topic + ".md"),
                markdown.TrimEnd() + "\n",
                new UTF8Encoding(false),
                token
            );
        }
        finally
        {
            _writes.Release();
        }
        Reindex();
    }

    public async Task AppendAsync(string day, string line, CancellationToken token)
    {
        if (!Day.IsMatch(day))
            throw new WorkspaceException("That note name isn't allowed.", 400);
        await _writes.WaitAsync(token);
        try
        {
            var path = Path.Combine(NotesFolder, day + "-hirezero.md");
            var header = File.Exists(path) ? "" : "# HireZero notes " + day + "\n\n";
            await File.AppendAllTextAsync(
                path,
                header + line.ReplaceLineEndings(" ") + "\n",
                new UTF8Encoding(false),
                token
            );
        }
        finally
        {
            _writes.Release();
        }
        Reindex();
    }

    public Task<MemoryOverview> OverviewAsync(CancellationToken token)
    {
        var workspace = Workspace;
        var longTerm = Path.Combine(workspace, "MEMORY.md");
        var dreams = Path.Combine(workspace, "DREAMS.md");
        var text = File.Exists(longTerm) ? File.ReadAllText(longTerm) : "";
        var notes = Directory.Exists(Path.Combine(workspace, "memory"))
            ? Directory
                .EnumerateFiles(Path.Combine(workspace, "memory"), "*.md", SearchOption.TopDirectoryOnly)
                .Select(file => new FileInfo(file))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(15)
                .Select(file => new MemoryFile(
                    "memory/" + file.Name,
                    file.Length,
                    new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero).ToString("O")
                ))
                .ToArray()
            : [];
        string? dreamTail = null;
        if (File.Exists(dreams))
        {
            var all = File.ReadAllText(dreams);
            dreamTail = all.Length > 1500 ? all[^1500..] : all;
        }
        return Task.FromResult(
            new MemoryOverview(
                text.Length > 6000 ? text[..6000] : text,
                text.Length > 6000,
                notes,
                dreamTail,
                "OpenClaw memory-core"
            )
        );
    }

    // Keyword indexing is local and quick; writes that land while it runs trigger one more pass.
    private void Reindex()
    {
        if (Interlocked.Exchange(ref _indexing, 1) == 1)
        {
            Volatile.Write(ref _indexAgain, 1);
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                do
                {
                    Volatile.Write(ref _indexAgain, 0);
                    try
                    {
                        await cells.CliAsync(
                            Cell,
                            ["memory", "index"],
                            TimeSpan.FromMinutes(2),
                            CancellationToken.None
                        );
                    }
                    catch (ModelUnavailableException error)
                    {
                        AgentActivity.Note("Memory indexing didn't finish: " + error.Message);
                    }
                } while (Volatile.Read(ref _indexAgain) == 1);
            }
            finally
            {
                Volatile.Write(ref _indexing, 0);
            }
        });
    }

    static string? Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
}
