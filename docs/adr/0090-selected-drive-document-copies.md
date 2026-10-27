# 0090: Import selected Google Drive documents as reviewed text copies

Date: 2026-10-26

Status: Accepted; local verification recorded separately. Live Google acceptance remains pending.

## Context

Private company knowledge required manual file upload even after the company had connected other services.
Drive is a useful additional source, but access to a connected account must not silently import unrelated files
or imply that approved copies continuously reflect the source.

## Decision

- Add a role-independent Google Drive document integration with company-owned settings and a dedicated service
  account key. Reuse the existing encrypted local secret store or company-specific hosted credential directory.
  Do not reuse Analytics OAuth tokens or request user impersonation/domain-wide delegation.
- Use the Drive read-only OAuth scope. The service account can access files shared with it; HireZero additionally
  enforces an exact, case-sensitive allowlist of at most 20 file IDs. Setup instructions require a dedicated
  account and Viewer sharing of selected files. Configuration/status inspection performs no external request.
- Owners explicitly request a single-file preview from Documents. Support Google Docs plain-text export, Word
  body/table extraction and UTF-8 text/Markdown downloads. Do not list Drive, traverse folders, follow shortcuts,
  follow redirects, acknowledge abusive files, or read arbitrary returned download URLs. Only fixed Google token
  and Drive API endpoints receive requests.
- Bound token/metadata replies to 32 KiB, text to 64 KiB including source details, Word downloads to 2 MiB and
  each serialized company preview to 30 seconds including queue wait. Reuse the Word parser's package/XML limits.
  Verify download permission, non-trashed state, supported type and exact ID. Compare metadata/version before
  and after content read; reject changed or incomplete copies. No automatic retries or partial-text fallback.
- Recheck the settings/key fingerprint before subsequent requests and before returning text. Host delivery also
  checks the company connection generation and the requesting owner's current role. Withhold results when
  authorization changes in flight. Audit a successful preview by integration/file ID, never document text or keys.
- Return a no-store editable preview, not a persisted document or model input. Include the source URL, Drive
  version, source modification time and read time in its text. This editable note is not immutable signed
  provenance. The existing document digest covers the exact text the owner subsequently saves and approves.
- Keep import, save and approval separate. The owner selects an audience; only approved copies enter its bounded
  context. A Drive copy is independent of its connection: source updates or disconnection do not rewrite or erase
  already saved documents. State this in setup and import UI. Existing document revocation/deletion controls apply.
- Preserve newer editor changes, failed-import drafts, source filename and audience through reload. Require an
  explicit retry after a failed read. A neighboring company starts with its own blank settings and credentials.

This extends [ADR 0062](0062-company-owned-private-connections.md) and
[ADR 0089](0089-reviewed-word-text-import.md). No new package, runtime upgrade, model call or payment action is needed.
Delegated OAuth/Picker onboarding, automatic private-source refresh, signed import provenance and broader private
connectors remain separate work. Service-account setup and actual Google access are not validated by fixtures.

API behavior is based on Google's [file export reference](https://developers.google.com/workspace/drive/api/reference/rest/v3/files/export),
[file read reference](https://developers.google.com/workspace/drive/api/reference/rest/v3/files/get),
[scope guide](https://developers.google.com/workspace/drive/api/guides/api-specific-auth) and
[export-format list](https://developers.google.com/workspace/drive/api/guides/ref-export-formats).

Verification: [selected Drive import receipt](../history/2026-10-26-selected-drive-document-copies.md).

Update: [ADR 0118](0118-private-document-source-receipts.md) adds company-bound application import receipts,
structured source attribution and explicit source comparisons with separately reviewed replacements. Earlier
editable source headers remain unverified; no historical receipt is fabricated from their text.
