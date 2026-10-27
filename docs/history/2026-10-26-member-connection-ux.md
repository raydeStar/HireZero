# Member connection status — 2026-10-26

The Integrations page mounted owner setup components for viewers and approvers. Those components requested
protected settings and account details, and Google continued polling its owner-only status endpoint every three
seconds. Conversation connection cards also offered setup and dismissal controls to members without that authority.
The server refused protected settings and mutations, but the UI made ordinary membership look like a broken
connection. The audit also found the GitHub sign-in status read missing the owner check used by the other providers.

The member view now shows pending requests first, then active connections, with disconnected options collapsed.
It explains that a company owner handles setup. It does not mount private forms or poll sign-in status. Conversation
cards use the same membership boundary. A resolved card's existing action retry remains available to approvers and
owners; viewers see its saved status. Owner setup and connection flows retain their existing behavior.
The sidebar also names the current member role instead of labelling every member as Owner.

This aligns the interface with [ADR 0020](../adr/0020-members-roles-and-sign-in.md) and
[ADR 0062](../adr/0062-company-owned-private-connections.md). The GitHub sign-in status read now enforces that
existing owner-only account-detail policy as well. The server enforces permissions independently of the interface.

## Verification

- Integration-test fixture build: zero warnings/errors.
- Web typecheck and production build: passed; the existing bundle-size advisory remains.
- Fifteen focused connection-isolation, connection-card and GitHub-status facts: passed, zero failures/skips.
- Extended browser journey: owner settings/credentials remain company-scoped, fictional Google consent returns to
  the initiating company, and sign-out persists. Viewer and approver memberships come from the actual disposable
  server, rather than forged session responses. Neither role requests private setup endpoints on the Integrations
  page or conversation cards, including after the former polling interval. Direct protected reads and dismissal
  attempts return 403, including GitHub sign-in details. Restoring the owner role restores setup; the refused
  dismissal leaves the request open. The displayed membership label agrees with the server role.
- Desktop (1440 pixels) and mobile (390 pixels) member layouts fit without horizontal overflow. Pending requests
  and available connections are visible before optional disconnected choices.

The initial focused test run encountered sandbox file/socket restrictions; the completed rerun used disposable
fixture files and loopback HTTP outside that restriction. It passed without live accounts, model calls or provider
requests. The browser fixture uses a fresh profile and removes its owned temporary files after both processes exit.

Private logs, screenshots and the JSON journey receipt are under ignored `test-results/`, with the
`integration-member-` prefix and `integration-isolation-browser.json`. This is local fictional evidence; real hosted
sign-in, provider authorization and customer acceptance remain separate. No WSL service, runtime image or application
execution setting changed in this pass. The prepared script source supplement was refreshed for the test-source
and host owner-check changes only; it has not been transferred or executed. The image/runtime candidate is unchanged.
