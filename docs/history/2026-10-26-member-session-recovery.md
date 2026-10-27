# Hosted member sessions and browser recovery — 2026-10-26

The lifecycle audit found a concrete browser boundary missing from the product: drafts and uncertain-operation
receipts were scoped to a company but not to the member using the tab. The hosted workspace also exposed no
sign-out control. A changed cookie could therefore leave a page displaying the previous member's inputs while
subsequent requests ran as the new member.

[ADR 0114](../adr/0114-member-bound-browser-sessions.md) now defines member-scoped hosted recovery storage,
identity-bound browser requests, session rechecks and verified sign-out. All fifteen company storage users share
the same wrapper. Local single-owner recovery keys remain compatible. Unattributed older hosted keys are kept,
but never automatically adopted by a newly signed-in account. Member drafts and uncertain IDs remain available to
the same account in the same tab after sign-out; this is not encryption or deletion of browser-profile data.

The server supplies an opaque identity label and refuses a mismatched supplied label before opening company data
or processing a command. The normal cookie, membership and role checks remain authoritative. The UI checks sessions
on focus/restoration, cross-tab sign-out notices and expired/changed-session API results. It hides the workspace
during checks without destroying the editor for an unchanged account; changed accounts or roles reload all memory.
The fallback for an inaccessible explicit company link now removes the stale query instead of repeatedly reloading it.

Sign-out immediately hides private content and stops further client requests. A separate verified session read
confirms cookie expiry, including after a lost POST response. A malformed or unavailable response keeps a retry
screen. Late reads are ignored, including audit downloads; late dispatched commands retain an uncertain outcome.
No previously authorized server work is canceled by signing out.

## Verification

- Test-host build: zero warnings/errors. Web typecheck and production build pass with the existing bundle-size advisory.
- Two hosted sign-in facts pass. The extended real middleware journey uses an in-process fictional OIDC provider:
  PKCE, secure cookies, uninvited refusal, two invited owners sharing one cookie jar, changed identity scopes,
  stale-member GET/POST refusal, current-member access and confirmed cookie sign-out.
- Fourteen client test entries pass: seven existing command/company-creation recovery entries plus seven member
  storage/API entries. They cover account separation, preserved uncertain receipts, local compatibility, frozen
  member/company headers, blocked delayed requests, late command uncertainty, late export refusal and no replay.
- The extended browser fixture retains its actual local viewer/approver and connection-isolation checks. Its hosted
  UI portion uses explicitly synthetic session transport against disposable records: different accounts cannot see
  each other's unfinished document drafts, returning to an account restores its draft, unchanged-session focus
  preserves the editor, a stale request triggers reauthentication, and another tab's sign-out hides the workspace.
  A failed sign-out followed by a malformed session response keeps the retry screen; a later lost sign-out reply
  is confirmed through a session read. This UI simulation is separate from the real OIDC middleware facts above.
- The local-owner business-drafting regression passes through discovery, corrections, hiring, draft/reload recovery,
  first-assignment approval, result review and completion with the new storage wrapper.
- Desktop (1440 pixels) and mobile (390 pixels) layouts are inspected. Sign-out is accessible in the header row,
  including on mobile, without horizontal page overflow. The test fixture reports no uncaught UI exceptions.

Private logs and images are under ignored `test-results/` with the `member-session-` prefix; the combined journey
receipt is `integration-isolation-browser.json`. No live identity provider, model, external account, payment or
deployment was used. Temporary fixture profiles and data are removed after their processes exit.

The prepared source supplement for a future approved script qualification is refreshed for the reviewed host and
test changes. The prior 549-file Linux source snapshot was checked for unexpected drift. The supplement has
22 entries and would produce 558 source files; no transfer or execution took place. The image/runtime candidate
and application execution settings remain unchanged. Full company erasure and production hosted acceptance
remain open in the delivery ledger.
