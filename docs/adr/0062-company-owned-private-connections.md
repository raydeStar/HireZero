# 0062: Company-owned private connections

Status: Accepted. Supersedes the shared company integration configuration in ADRs 0038/0039/0049.

## Decision

Each company runtime owns its Google Analytics/Search Console, Google Ads, OpenRouter and Microsoft 365
provider instances, settings and credential readers. Background jobs close over that runtime; they do not
resolve a company from ambient HTTP context. Saving configuration activates tools only in that company.
The host no longer installs pasted integration keys into the process-wide `SecretSource.Default`.

New local companies and every hosted company use
`companies/<lowercase SHA-256 of the company ID>/integrations/` beneath the host data root. Their encrypted
credentials use a company-specific Data Protection purpose as well as a separate directory. Copying an
encrypted credential to another company cannot make it readable there. Hosted connections read named files
from their own `credentials/` directory, with no fallback to process environment, system credentials or
another company's pasted keys. Host model/search/notification service credentials remain operator settings;
this change does not offer tenant model BYOK or alter those service boundaries.

For compatibility, only the original company in **local mode** retains the old root configuration and
credential precedence. Existing local authorization is not copied into newly created companies. Hosted
upgrades deliberately do not import old root-level private connections: an operator must provision each
intended company and the owner must reconnect Microsoft 365. No credential or private data is deleted by
the upgrade.

## Machine access

GitHub CLI sign-in, Git/local process tools, Azure CLI organizations, coding-app histories and the legacy
Jev connection are available only to the original local company. They are not tenant-safe hosted connectors.
Other companies see an explicit unavailable explanation; they cannot invoke the machine GitHub sign-in
endpoint or configure the machine-history integrations. Separate GitHub/Azure/coding-app connectors remain
product work. Synthetic data can still be enabled explicitly and contains no private account data.

## Consent and revocation

Google and Microsoft sign-in starts require the company's current owner. A pending one-time state binds the
company runtime, initiating owner, settings generation, verifier and ten-minute expiry. Each provider keeps
at most one current consent attempt per company. Success, cancellation and failure consume the state.
Settings changes and disconnect invalidate pending consent. Completion rechecks the owner's membership
before the exchange and before storing credentials, serializes with configuration changes, and returns to
an explicit company URL. Callback errors are generic and cannot expose upstream account details.

Settings and secret write operations remain explicit owner actions. Google sign-out removes only the
company's app-stored OAuth refresh token; a separate service account can remain active, which the UI explains.
A token delivered outside the app must be removed at its source; the endpoint refuses to claim successful
sign-out otherwise. Registered providers recheck credentials at use. Google access-token cache identity
includes the client ID, client secret and refresh token. Removing credentials never switches real tools back
to fictional data during that runtime.

Microsoft settings changes disconnect the existing account so new client/scopes require consent. A late
refresh uses compare-and-save against the prior account and cannot restore a disconnected or replaced
account. These local revocations prevent later application reads; they do not promise upstream grant deletion
or erase earlier imported evidence. Reads already sent to a provider can finish.

## UX and limits

The integration page states that connections belong to the current company. Setup links focus the actual
configuration form. Google offers sign-out and a company-specific result message. Callback routes retain
the selected company even if another tab selected a different company.

Configuration and account-detail reads require the owner; ordinary company capabilities expose availability.
Real Google/Microsoft authorization, hosted deployment/secret delivery, upstream revocation, tenant model
BYOK and developer sandbox isolation require separate verification. Provider tests and a fake-provider
browser journey prove local mechanics, not real account access or complete product-phase acceptance.
