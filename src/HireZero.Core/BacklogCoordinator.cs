namespace HireZero.Core;

// Bounded local catch-up only. No model, network client, agent queue or execution dispatcher is available here.
public sealed class BacklogCoordinator(BacklogStore store, IEnumerable<IBacklogSource> sources)
{
    private readonly IBacklogSource[] _sources = sources.OrderBy(source => source.Info.Order).ToArray();
    private readonly SemaphoreSlim _run = new(1, 1);
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    private BacklogSourceInfo Selected(IBacklogSource source) => store.Selected(source.Info);

    public BacklogDashboard Dashboard()
    {
        var now = Clock();
        var views = _sources
            .Select(source =>
            {
                var info = Selected(source);
                var checkpoint = store.Checkpoint(info);
                var freshness = !info.Ready
                    ? info.Configured
                        ? "Disabled"
                        : "Unavailable"
                    : checkpoint.State == "caught-up"
                        ? checkpoint.Gaps is { Length: > 0 }
                            ? "Checked with known gaps"
                            : !checkpoint.AuthoritativeSnapshot
                                ? "Checked without full membership confirmation"
                                : checkpoint.LastComplete is { } complete && now - complete <= TimeSpan.FromMinutes(5)
                                    ? "Retained metadata checked recently"
                                    : "Needs catch-up"
                        : checkpoint.State == "never-checked"
                            ? "Never checked"
                            : checkpoint.State;
                return new BacklogSourceView(info, checkpoint, freshness);
            })
            .ToArray();
        var selected = views
            .Where(view => view.Info.Ready)
            .ToDictionary(view => view.Info.Id, view => view.Info.ScopeRevision);
        return new(
            views,
            store
                .Inbox()
                .Where(item => selected.TryGetValue(item.SourceId, out var revision) && revision == item.ScopeRevision)
                .ToArray(),
            store
                .Proposals()
                .Where(item => selected.TryGetValue(item.SourceId, out var revision) && revision == item.ScopeRevision)
                .ToArray(),
            store.PreviousSeen(),
            now,
            [
                "No global history guarantee. Only explicitly selected, currently retained local metadata is scanned.",
                "Deleted records, expired provider history and events never received while this host was offline cannot be recovered here.",
                "Source freshness describes this local scan, not a live GitHub, Microsoft, Codex or Claude connection.",
                "Views show at most 200 stored revisions and proposals; a full retained-source scan is separate from that display limit.",
                "Current plan approval requires completed local membership confirmation. Unavailable source records remain readable as history.",
                "No model call, provider transmission, repair, publication or remote worker is connected to backlog plans.",
            ]
        );
    }

    public void Select(string id, bool enabled)
    {
        var source =
            _sources.SingleOrDefault(source => source.Info.Id == id)
            ?? throw new WorkspaceException("Unknown backlog source.", 404);
        if (enabled && !source.Info.Ready)
            throw new WorkspaceException("This source has no approved local metadata adapter.");
        store.Select(source.Info, enabled, Clock());
    }

    public BacklogProposal Plan(string id, BacklogPlanInput input)
    {
        Dashboard();
        return store.Plan(id, input, Clock());
    }

    public BacklogProposal Approve(string id, string snapshot, string owner)
    {
        Dashboard();
        return store.Approve(id, snapshot, owner, Clock());
    }

    public async Task<BacklogDashboard> CatchUpAsync(CancellationToken token)
    {
        if (!await _run.WaitAsync(0, token))
            throw new WorkspaceException("A local catch-up is already running.", 409);
        try
        {
            foreach (var source in _sources)
            {
                for (var pageNumber = 0; pageNumber < 4; pageNumber++)
                {
                    token.ThrowIfCancellationRequested();
                    var info = Selected(source);
                    if (!info.Ready)
                        break;
                    var now = Clock();
                    var before = store.Checkpoint(info);
                    if (before.LeaseUntil is { } held && held > now)
                        break;
                    var lease = before with
                    {
                        State = "catching-up",
                        Through = before.Through ?? now,
                        Since = before.Through is null ? before.LastComplete : before.Since,
                        LastAttempt = now,
                        LeaseUntil = now.AddMinutes(2),
                        Note = null,
                    };
                    if (!store.Replace(before, lease))
                        break;
                    try
                    {
                        var read = new BacklogRead(lease.Cursor, lease.Since, lease.Through!.Value, 32);
                        var page = await source.ReadAsync(read, token);
                        BacklogPolicy.Validate(page, read);
                        if (Selected(source).ScopeRevision != info.ScopeRevision)
                            break;
                        if (
                            !store.CommitPage(lease, page, Clock(), (source as IBacklogSnapshotSource)?.MembershipSql)
                            || page.Complete
                        )
                            break;
                    }
                    catch (Exception error)
                        when (error is not OperationCanceledException || !token.IsCancellationRequested)
                    {
                        store.Replace(
                            lease,
                            lease with
                            {
                                State = "failed",
                                LeaseUntil = null,
                                Failures = lease.Failures + 1,
                                Note =
                                    "The selected local source could not complete this page. Its cursor was retained; retry local catch-up.",
                            }
                        );
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        store.Replace(lease, lease with { State = "interrupted", LeaseUntil = null });
                        throw;
                    }
                }
            }
            store.Touch(Clock());
            return Dashboard();
        }
        finally
        {
            _run.Release();
        }
    }
}

public sealed class BacklogStartup(BacklogStore store, BacklogCoordinator coordinator) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        store.Startup(DateTimeOffset.UtcNow);
        await coordinator.CatchUpAsync(stoppingToken);
    }
}

public sealed class UnavailableBacklogSource(string id, string title, int order, string reason) : IBacklogSource
{
    public BacklogSourceInfo Info => new(id, title, order, false, false, null, reason);

    public Task<BacklogPage> ReadAsync(BacklogRead read, CancellationToken token) =>
        throw new WorkspaceException("No local adapter is configured.");
}
