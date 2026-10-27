# Drive file selection verification — 2026-10-26

Drive setup now accepts ordinary Google Docs/Drive file links and saves their exact file IDs. The shared
settings writer previously deduplicated all lists without considering case, silently collapsing distinct
Drive IDs. The Drive field now requests case-sensitive identity while other fields keep their existing defaults.
See [ADR 0123](../adr/0123-drive-links-and-exact-file-identities.md).

Verification: both affected test projects built with zero warnings/errors; eight Google Drive adapter facts
and three company/setup facts passed with zero skips. Coverage includes link parsing without network access,
rejected origins/routes/credentials/ports, exact-case identities, deduplication of equivalent links, the 20-file
limit, preserved settings after refusal and the existing owner/company/revocation boundaries.

The full fictional Drive browser journey passed with a pasted Google Docs URL plus its duplicate raw ID.
The saved allowlist contains one exact ID and setup performs no private reads. The existing preview, draft
preservation, separate approval, source comparison, replacement and tenant-isolation paths continue to pass.
No real Google account, credential, document or provider was used. Delegated sign-in remains separate work.

Private logs are retained under ignored `test-results/drive-link-*.log`. Fixture processes exited and their
temporary output was removed. This improves P1.6/P2.5/P3.4 usability and exact scope handling without claiming
live provider or customer acceptance.
