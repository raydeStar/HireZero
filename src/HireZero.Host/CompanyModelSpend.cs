using System.Text;
using HireZero.Integrations.Codex;
using HireZero.Integrations.ModelApis;

public sealed partial class CompanyModelConnection
{
    public CompanyStore? SpendStore { get; set; }

    public void DemandCappedHost()
    {
        if (Current.Mode != "host" || platform is CompanyCodexConnection)
            throw new InvalidOperationException(
                "Open sign-up spending caps require the configured host API model; company keys and Codex are unavailable."
            );
    }

    // This boundary covers text, drafts, analysis and scheduled replies alike. Transport remains single-shot.
    private async Task<ModelReply> WithSpendAsync(
        ModelCall call,
        int output,
        Func<Task<ModelReply>> send,
        CancellationToken token
    )
    {
        if (SpendStore is not { } store)
            return await send();
        token.ThrowIfCancellationRequested();
        if (TermsFor(call.ModelRoute ?? "").AllowanceUnit != "usd-cents" || output <= 0)
            throw new ModelTransportException(
                "Free allowance requires a priced API model with a bounded output.",
                0,
                usage: new(0, 0)
            );
        var price =
            Price(call.ModelRoute ?? "")
            ?? throw new ModelTransportException(
                "Free allowance requires an operator-reviewed model price.",
                0,
                usage: new(0, 0)
            );
        var bytes =
            Encoding.UTF8.GetByteCount(CompanyJson.Encode(call.Context))
            + Encoding.UTF8.GetByteCount(call.Instructions);
        var worst = ApiModelProvider.WorstCaseUsd(price, bytes, output);
        store.ReserveModelSpend(call.DispatchId, worst);
        void Settle(ModelUsage? usage, long? total, bool fault = false)
        {
            if (!fault && usage is not null && usage.Consistent(total) && usage.BilledUsd is not < 0)
                store.SettleModelSpend(call.DispatchId, usage.BilledUsd ?? price.Cost(usage));
            // An accounting fault keeps the worst-case hold, unless the provider reports spending more than that.
            else if (
                fault
                && usage is not null
                && (usage.BilledUsd ?? price.Cost(usage)) is var reported and > 0
                && reported > worst
            )
                store.SettleModelSpend(call.DispatchId, reported);
        }
        try
        {
            // Cancellation before dispatch is known unsent. After dispatch it keeps the hold unless usage is known.
            if (token.IsCancellationRequested)
            {
                store.SettleModelSpend(call.DispatchId, 0);
                token.ThrowIfCancellationRequested();
            }
            var reply = await send();
            Settle(reply.Usage, reply.TotalTokens);
            return reply;
        }
        catch (WorkspaceException) when (platform is ApiModelProvider)
        {
            // The API adapter uses WorkspaceException only for its pre-send authorization check.
            store.SettleModelSpend(call.DispatchId, 0);
            throw;
        }
        catch (ModelTransportException error)
        {
            Settle(error.Usage, error.KnownUsage, error.AccountingViolation);
            throw;
        }
    }
}
