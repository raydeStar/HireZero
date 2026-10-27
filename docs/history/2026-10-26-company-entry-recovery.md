# Company entry and invitation recovery — 2026-10-26

The hosted-entry audit found a sign-in dead end: invitations could bind in a secondary company, but session
bootstrap and company discovery first required membership in the default company. A secondary-only member could
authenticate without reaching their workspace. Falling back from a stale company link did not resolve this.

[ADR 0115](../adr/0115-authenticated-company-entry-recovery.md) adds an authenticated selection response when the
selected company is absent or inaccessible. Its directory lists only active memberships. Ordinary workspace reads,
creation and commands still enforce company membership and roles; stale browser-member markers are still refused.
Session and directory responses are not cached.

The browser validates each choice before rendering and mounts no workspace while selection is required. An
explicit company choice navigates to its home, removing previous task/view parameters. A revoked member gets
an explanation, refresh and hosted sign-out. Session rechecks clear the old mounted state before recovery. Single,
multiple and empty company lists have separate, usable states. Buttons meet the tested touch-target sizes and
long company names wrap. Entry-screen padding also removes the blank strip and unnecessary vertical scrolling
caused by collapsed margins.

## Verification

- Test-host build passes with zero warnings/errors. Web typecheck and production build pass; the existing bundle-size
  advisory remains.
- Three hosted sign-in facts pass using a fictional, in-process OIDC provider and real middleware. The added fact
  covers secondary-only invitation binding, root and missing-company bootstrap, filtered directory, no-store,
  stale-marker refusal, default-company refusal, owner-only creation refusal, explicit secondary-company access,
  membership revocation, empty-directory recovery and cookie sign-out.
- Two existing company-isolation facts pass, including actual company-scoped HTTP requests.
- Fourteen client recovery test entries pass, preserving member draft separation, uncertain receipts and no replay.
- The complete integration/member browser journey passes. Its actual local membership, connection and role checks
  remain intact. The explicitly synthetic hosted-session transport then exercises single/multiple selection,
  fresh company navigation without old task parameters, revocation while a workspace is mounted, restored access,
  empty-list sign-out, malformed choices and absence of workspace requests before selection.
- Desktop (1440 pixels) and mobile (390 pixels) screenshots are inspected. Choice buttons are at least 48 pixels
  high; refresh/sign-out targets are at least 44 pixels. Both layouts fit horizontally and vertically without
  unnecessary scrolling. The fixture reports no uncaught UI exceptions.

Private logs and screenshots are retained under ignored `test-results/` with the `company-entry-` prefix. The final
browser log is `company-entry-browser-final.log`; `integration-isolation-browser.json` includes the entry/revocation
flags. Temporary test profiles and databases are removed after their processes exit. No live identity provider,
model, external account, payment or deployment was used.

The prepared script-qualification source supplement remains 22 entries, producing 558 source files if later applied.
The existing 549-file Linux snapshot was checked for unexpected source drift. Only the prepared hashes were updated;
no Linux transfer, image build, runtime job or application activation occurred. Separate script qualification
approval and production hosted/customer acceptance remain outstanding.
