# 0114: Keep hosted browser drafts and requests with their member

Date: 2026-10-26

Status: Accepted; extends ADRs 0020, 0056 and 0107. Verification is recorded separately.

## Context

Company-scoped browser drafts and recovery receipts were shared by any account using the same tab. Hosted sign-in
had no corresponding sign-out control in the workspace. A stale page could also submit its old content after the
browser cookie changed to another invited member. Server membership checks alone do not identify which account
the page was displaying when the user chose an action.

## Decision

- Return a stable SHA-256-derived `browserScope` for the authenticated subject in the session response. It is an
  identity label, not a credential or authorization grant. Existing session cookies and company-role checks remain
  authoritative. The browser verifies the session shape, signed-in state, role and scope before mounting a workspace.
- Put hosted tab drafts and recovery receipts under `hirezero-member-v1:<scope>:` plus their existing company and
  feature keys. Apply this consistently to document, memory, business, hiring, first-assignment, scheduling, billing,
  usage, inbox, task-cost, command and company-creation storage. The single-owner local mode retains its existing
  keys and recovery behavior. Hosted mode never automatically adopts unattributed keys from older versions.
- Preserve member-scoped drafts and uncertain operation IDs when signing out. They remain in that tab's session
  storage for the same account's recovery. Old unattributed entries are also preserved but are not displayed or
  replayed. This is UI isolation, not encryption or protection against someone inspecting the browser profile or
  developer tools. It is not a claim of data erasure, and it does not change company database retention.
- Freeze the loaded member marker on API requests, alongside the existing company marker. If a supplied marker
  differs from the current authenticated subject, refuse before accessing company data or processing a command,
  returning `409 session-changed`. Session checks omit the marker so they can discover the current identity.
  Legacy/API clients without the marker still require ordinary authentication and authorization; the marker itself
  grants no access. Audit exports use the same API path and checks.
- Recheck hosted sessions on focus, visibility restoration, page restoration, sign-out notices from other tabs and
  expired/changed-session API responses. Hide the workspace during checks while retaining mounted input state for
  an unchanged session. A changed identity or role reloads the page, clearing all in-memory caches before reuse.
  Failed checks show a retry screen. An inaccessible explicit company link falls back to `/`, removing the stale
  query rather than reloading it forever.
- Add an accessible sign-out control to the desktop and mobile header. Immediately unmount private content and stop
  new client requests. Send the existing CSRF-protected cookie-expiry POST, then independently read the session.
  Only an explicit verified hosted signed-out response confirms completion. A failure, malformed response or
  uncertain result keeps the workspace hidden and offers retry. The successful transition signals other tabs.
- Ignore reads arriving after their member binding changed. A command already dispatched before sign-out may have
  succeeded; retain an unknown outcome and its receipt rather than pretending it was refused or resending it.
  Signing out does not cancel previously authorized server work.

## Limits

These changes cover the application UI, session cookie and browser recovery boundary. They do not sign out of the
identity provider globally, revoke other devices, delete browser-profile files, erase company records or qualify a
production identity provider. Existing deployments should account for unattributed legacy tab drafts before an
upgrade; this change cannot safely infer their author. Real hosted acceptance remains separate from fictional OIDC
and browser transport tests.
