namespace HireZero.Core;

public sealed partial class CompanyScheduler
{
    // The host supplies a role-neutral tool path. Core never imports a web integration.
    public Func<CompanyCommand, string, CancellationToken, Task<string>>? RefreshScheduledSources { get; set; }
    private readonly SemaphoreSlim _schedules = new(1, 1);

    public async Task<bool> TickSchedulesAsync(CancellationToken token = default)
    {
        if (!await _schedules.WaitAsync(0, token))
            return false;
        try
        {
            var run = store.ClaimDueSchedule();
            if (run is null || run.State == "missed")
                return false;
            var schedule = store.ScheduleDetail(run.ScheduleId).Schedule;
            try
            {
                if (schedule.Definition.Kind == "source-refresh")
                {
                    if (RefreshScheduledSources is null)
                        throw new WorkspaceException(
                            "Source refresh integration is unavailable. Review this schedule before resuming."
                        );
                    var (command, subject) = store.ScheduledSourceCommand(run.Id);
                    var receipt = await RefreshScheduledSources(command, subject, token);
                    store.FinishScheduleRun(run.Id, CompanyJson.Decode<CompanySourceRefresh>(receipt).Id);
                }
                else if (schedule.Definition.Kind == "script-task")
                {
                    // Admission is transactional; the existing isolated worker claims the saved packet once.
                    store.StartScheduledSandbox(run.Id);
                }
                else if (schedule.Definition.Kind == "browser-task")
                {
                    store.StartScheduledBrowser(run.Id);
                }
                else
                {
                    if (
                        model is not IMeasuredModelProvider measured
                        || measured.RuntimeIdentity != schedule.Authority.RuntimeIdentity
                    )
                        throw new WorkspaceException("The schedule's approved measured provider is unavailable.");
                    await store.ProviderGate.WaitAsync(token);
                    try
                    {
                        var attempt = store.StartScheduledTask(run.Id);
                        try
                        {
                            var call = store.TextCall(attempt.Id);
                            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                            deadline.CancelAfter(TimeSpan.FromSeconds(attempt.Measured!.Policy.LocalDeadlineSeconds));
                            var reply = await CompleteCurrentAsync(
                                call,
                                deadline,
                                (request, cancel) => measured.CompleteMeasuredAsync(request, attempt.Measured!, cancel)
                            );
                            store.Finish(attempt.Id, reply);
                        }
                        catch (ModelTransportException error)
                        {
                            store.RejectResponse(attempt.Id, error);
                        }
                        catch (Exception error) when (error is not OutOfMemoryException)
                        {
                            store.FailAttempt(attempt.Id, "provider-accounting-unconfirmed", true);
                        }
                        store.FinishScheduleRun(run.Id);
                    }
                    finally
                    {
                        store.ProviderGate.Release();
                    }
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                store.FinishScheduleRun(
                    run.Id,
                    error: error is WorkspaceException
                        ? ContentGuard.Clean(error.Message, 500).Text
                        : "The scheduled occurrence did not finish. It was not replayed; review its saved status before resuming."
                );
            }
            return true;
        }
        finally
        {
            _schedules.Release();
        }
    }
}
