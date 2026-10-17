using Microsoft.Data.Sqlite;

namespace HireZero.Core;

// The durable permission row is the recovery source after acceptance/allow succeeded but enqueue failed.
// Bounded scans only repair reply tasks; they grant no PR execution or publication authority.
public sealed class RequestReplyRecovery(
    RequestStore requests,
    AgentStore agents,
    WorkspaceOwner owner,
    AgentWorker? worker = null
) : BackgroundService
{
    private string _cursor = "";

    public int Reconcile(AgentWorker? worker = null)
    {
        var batch = requests.GrantedRequestsAfter(_cursor);
        if (batch.Length == 0)
        {
            _cursor = "";
            return 0;
        }
        var count = 0;
        foreach (var item in batch)
        {
            _cursor = item.Id;
            try
            {
                requests.WithAgentPermission(
                    item,
                    owner,
                    () =>
                    {
                        var current = requests.FindRequest(item.Id);
                        if (current is null || RequestStore.AgentRequestClosed(current.Status))
                            return false;
                        if (
                            requests.PendingReplyKey(item.Id) is { } pending
                            && agents.RecoverMissingReply(
                                item.Id,
                                current.DisplayTitle,
                                requests.ReplyQueueKey(item.Id, pending),
                                pending,
                                owner.Login,
                                requests.AgentConsentGeneration(item.Id)
                            )
                                is not null
                        )
                        {
                            count++;
                            worker?.Wake();
                        }
                        return true;
                    }
                );
            }
            catch (WorkspaceException) { } // Declined requests or revoked requesters cannot be revived.
            catch (SqliteException) { } // Durable grant remains available for the next bounded scan.
        }
        if (batch.Length < 32)
            _cursor = "";
        return count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Reconcile(worker);
            }
            catch (SqliteException) { }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
