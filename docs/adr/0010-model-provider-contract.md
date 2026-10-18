# 0010: The company provider contract is the model provider contract

Status: Accepted (Phase 2B.4). Supersedes the "Provider contract" section of
[ADR 0008](0008-model-routes.md).

## Context

ADR 0008 planned one new abstraction to replace four: `IModelGateway`, `ICompanyModel`, `ICompanyMeasuredModel` and
`ICodingRunner`. Looking closely in Phase 2B.4 showed two things.

First, the company path already has the right contract and the semantics that matter:

- exact input admission that refuses unknown runtime overhead;
- complete usage and receipts, or a transport failure carrying whatever is known;
- withholding of tool requests and invalid output;
- the scheduler's authority watch, bounded drain, termination hold and late-receipt settlement;
- the measured one-job extension with a pinned runtime identity.

Second, `IModelGateway` (14 call sites) and `ICodingRunner` (about 25) serve only the legacy Developer profile,
which Phase 2B.5 ports or retires. Migrating them first would do that work twice.

Also, the legacy budget ledger recorded a model call or coding run without reported usage as zero tokens.

## Decision

- **One contract.** The company contract becomes the platform's model provider contract, renamed and gathered in
  `src/HireZero.Core/ModelProviders.cs`:
  - `IModelProvider`, `IMeasuredModelProvider`, `ModelCall`, `ModelReply`, `ModelUsage`, `ModelInputAccounting`,
    `ModelTransportException` and `UnavailableModelProvider`.
  - A new model vendor is an integration that implements it ([how-to](../ADDING_A_MODEL_PROVIDER.md)).
- **Runtime constraints belong to the provider.** For example, the Codex connection accepts only OpenAI routes on
  the Codex runtime. Core's routing validation is generic, and the runtime for a purpose comes from the route
  catalog.
- **The legacy contracts are not extended.** `IModelGateway` and `ICodingRunner` stay as they are until the legacy
  profile retires. When the Developer is ported, its company coding runs move to an execution mode of the provider
  contract.
- **No usage is silently zero.** The ledger flags entries whose provider reported no usage:
  - model calls are recorded at a conservative estimate (input at about four characters per token, plus the full
    output cap);
  - coding runs are recorded at zero tokens, still counting their run and minutes;
  - the budget panel shows how many entries are unreported.

**Correction to ADR 0008.** A data root overrides routes with `HIREZERO_MODEL_ROUTES`, naming an owner-maintained
file. HireZero does not look for a `model-routes.json` inside the data root, as ADR 0008 says.

## Consequences

- Adding a model vendor touches one integration project, plus the company profile's provider selection in
  `CompanyWorkspace` (a single composition point).
- Phase 2B.5 deletes `IModelGateway`, `UsageMeter` and the legacy ledger paths along with the legacy profile, so the
  company reservation and settlement path is the only usage path left.
