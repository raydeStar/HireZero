using System.Collections.Concurrent;

// Company and owner come from the authenticated start request, never a callback query or another browser tab.
public sealed class CompanyConnectionSignIns(string provider)
{
    public sealed record Ticket(
        CompanyIntegrationScope Scope,
        string Subject,
        long Version,
        string Verifier,
        DateTimeOffset Expires
    )
    {
        public void DemandCurrent()
        {
            CompanyAccess.Demand(Scope.Store.Actor(Subject), CompanyRoles.Owner, "finish connecting this company");
            if (Scope.Version != Version)
                throw new WorkspaceException("Connection settings changed. Start sign-in again.", 409);
        }
    }

    private readonly ConcurrentDictionary<string, Ticket> _pending = new(StringComparer.Ordinal);

    public string Begin(
        CompanyIntegrationScope scope,
        string subject,
        Func<(string Url, string State, string Verifier)> begin
    )
    {
        scope.Gate.Wait();
        try
        {
            CompanyAccess.Demand(scope.Store.Actor(subject), CompanyRoles.Owner, "connect this company");
            foreach (var item in _pending.Where(p => p.Value.Scope == scope || p.Value.Expires < DateTimeOffset.UtcNow))
                _pending.TryRemove(item.Key, out _);
            var sign = begin();
            _pending[sign.State] = new(
                scope,
                subject,
                scope.Version,
                sign.Verifier,
                DateTimeOffset.UtcNow.AddMinutes(10)
            );
            return sign.Url;
        }
        finally
        {
            scope.Gate.Release();
        }
    }

    public async Task<string> CompleteAsync(
        string? state,
        string? code,
        string? error,
        Func<Ticket, string, CancellationToken, Task> complete,
        CancellationToken token
    )
    {
        if (state is null || !_pending.TryRemove(state, out var sign))
            return "/?view=departments&section=integrations&" + provider + "=expired";
        var target = sign.Scope.ReturnTo(provider);
        if (code is null || error is not null || sign.Expires < DateTimeOffset.UtcNow)
            return target + "cancelled";
        await sign.Scope.Gate.WaitAsync(token);
        try
        {
            sign.DemandCurrent();
            await complete(sign, code, token);
            sign.Scope.Activate();
            return target + "connected";
        }
        catch (Exception failure)
            when (failure
                    is WorkspaceException
                        or HttpRequestException
                        or InvalidOperationException
                        or System.Text.Json.JsonException
            )
        {
            // Provider errors can contain account details. Keep this receipt generic and never replay a code.
            return target + "failed";
        }
        finally
        {
            sign.Scope.Gate.Release();
        }
    }
}
