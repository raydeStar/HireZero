# Adding a model provider

A model vendor (Anthropic, Azure OpenAI, a local model server, ...) is an integration project that implements
`IModelProvider` ([ADR 0010](adr/0010-model-provider-contract.md)). Core's scheduler handles admission, budgets,
authority, deadlines and settlement. The provider's job is to keep the contract below exactly; the company's safety
guarantees depend on it.

## Existing HTTP vendors

`HireZero.Integrations.ModelApis.ApiModelProvider` serves Anthropic, OpenAI, OpenRouter and Vultr. Vultr's route is
`vultr/deepseek-v4.1-flash-1017`; its reviewed price is $0.15 per million input tokens and $0.60 per million output
tokens, including reasoning, in `config/model-routes.json`. The vendor reads `VULTR_INFERENCE_API_KEY` from
`SecretSource` and sends one bearer-authenticated `chat/completions` request to
`https://api.vultrinference.com/v1/`. `HIREZERO_VULTR_BASE_URL` may override the base URL with HTTPS, or HTTP on
loopback for fixtures. It never follows redirects or substitutes OpenRouter when the Vultr key is missing.

Vultr requests carry `model`, `messages`, `max_tokens` and `reasoning_effort=low`. New Vultr reviews include a
24,000-token output limit and a 360-second deadline; the scheduler reserves the reviewed planning tokens and the
adapter checks the worst-case spend before sending. Prompt, completion and reasoning usage settle through the
existing company ledger. Tool/function requests, truncation, excess output, missing receipts and incomplete usage
never become accepted text. Uncertain outcomes are held, without retry.

Like the other API vendors, Vultr is unqualified for automatic work: `CompleteAsync` refuses because `ModelCall`
carries no consumed measured authorization or spending allowance. `CompleteMeasuredAsync` serves approved work
after validating its exact route, context, instructions, policy and expiry. Do not make the ordinary entry point a
spending bypass. Approved recurring work uses the measured path too.

`HIREZERO_MODEL_VENDOR=vultr` activates the install-wide boundary in `CompanyWorkspace`; see
[SETTINGS.md](SETTINGS.md). Pass its captured `ModelVendorPolicy` into every provider construction, including
optional decision/tool transports and per-company adapters. Standalone adapters also read the environment by
default. Restricted constructors must refuse before network or runtime work begins. Unsupported execution roles
advertise their unavailability before hiring. The setting is off by default.

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
