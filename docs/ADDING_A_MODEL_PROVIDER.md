# Adding a model provider

A model vendor (Anthropic, Azure OpenAI, a local model server, ...) is an integration project that implements
`IModelProvider` ([ADR 0010](adr/0010-model-provider-contract.md)). Core's scheduler handles admission, budgets,
authority, deadlines and settlement. The provider's job is to keep the contract below exactly; the company's safety
guarantees depend on it.

## 1. Create the integration

`src/HireZero.Integrations.<Vendor>/HireZero.Integrations.<Vendor>.csproj`, referencing Core only. Keep endpoints and
credentials in the integration and in data-root configuration, never in source.

## 2. Implement the contract

```csharp
public sealed class VendorProvider(VendorOptions options) : IModelProvider
{
    public bool Qualified => true;            // false until isolation and usage accounting are proven
    public string Status => "Vendor X, pinned to route ...";

    public ModelInputAccounting CountInput(ModelCall call) => ...;
    public Task<ModelReply> CompleteAsync(ModelCall call, CancellationToken token) => ...;
}
```

What each member must do:

- **`CountInput`** counts every token the provider will send, including framing and instructions the runtime adds.
  If it cannot count the runtime's overhead exactly, return `RuntimeOverheadTokens: null`. Admission then refuses
  instead of guessing. Never return an observed average as a bound.
- **`CompleteAsync`** sends only what `ModelCall` admits:
  - the instructions and context;
  - no tools, history or retrieval the call did not include;
  - the exact `ModelRoute`, with no fallback model.

  It returns a `ModelReply` with the body, `TotalTokens`, a provider `Receipt` and `ModelUsage`.
- **Failures** throw `ModelTransportException` with whatever usage and receipt are known. The scheduler settles
  known usage and holds the reservation when usage is unknown.
- **Withhold, never return:** tool or function calls, truncated output, missing or inconsistent usage, and output
  over `MaxOutputTokens`.
- **Cancellation** stops your own in-flight request when the token is cancelled. The scheduler waits a bounded
  time, then holds the provider if termination is not confirmed. Do not retry; an uncertain outcome is never
  replayed.

Optional: implement `IMeasuredModelProvider` for the owner-approved one-job path. It needs a pinned
`RuntimeIdentity`, and a `CompleteMeasuredAsync` that re-checks the job's digests (context, instructions, route,
expiry) before sending. `CompanyCodexConnection` is the reference.

## 3. Route to it

- Add or change the purposes it serves in `config/model-routes.json`, under `routes`, `runtimes` and `names`.
- Refuse, at construction, any route or runtime your provider cannot serve, as `CompanyCodexConnection` does for
  OpenAI-on-Codex.

## 4. Select it in the company profile

Construct it in `CompanyWorkspace`, where the profile picks its `IModelProvider`; that is the one composition point.
Keep it behind explicit configuration: an unconfigured workspace stays on `UnavailableModelProvider`.

## 5. Test it offline

- Run the provider against a loopback HTTP fixture or a fake runtime, never the live service. Cover:
  - complete usage;
  - missing usage;
  - a tool call;
  - truncated output;
  - a server error;
  - a disconnect;
  - cancellation mid-request.
- `ScopedCompanyText` and its fixtures in `tests/HireZero.Company.Tests` are the pattern to follow.
- A live qualification is a separate, owner-approved step with an explicit token and spend cap.
