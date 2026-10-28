# Full-request context retrieval

## Reproduced failure

Two focused facts failed against the prior implementation: an approved document about the subject near the end
of a detailed request was absent, and employee-scoped approved memory about the final term of a 601-term request
was absent. Both requests fit the existing input bounds. The first-24-term cutoff caused both omissions.

## Correction and verification

[ADR 0162](../adr/0162-search-the-complete-bounded-request.md) searches the entire bounded request and abbreviates
only the query preview. It preserves the existing indexed ranking, audience checks, approval/freshness checks,
three-excerpt/three-memory ceilings, document length limit, immutable captured contexts and model packet budget.

- Both reproduced failures now pass. The document fact also verifies the exact excerpt reaches the captured model
  packet; the memory fact verifies a neighboring employee and company-only view cannot retrieve the private record,
  and revocation removes it immediately. Query metadata stays compact.
- All 45 focused document and memory facts pass, including isolation, approval, revocation, bounded excerpts,
  immutable capture and packet packing. All 23 schedule facts pass.
- A release compatibility smoke used the current installed host, integrations, web assets and dependencies with
  only the rebuilt core DLL and its symbols replaced. The unchanged assembly binding identity is verified.
- That staged host ran in an isolated temporary fictional company with execution disabled. Through its real HTTP
  API it saved and approved one fictional document and one memory record, then retrieved each using the topic at
  the end of a 601-term query. It returned one exact hit per source and the new method labels. The owned process
  exited and its temporary data was removed; compact logs and the receipt remain private.
- Release core build and required C#/web formatting pass. No dependency restore, package change, model request,
  external service request or GPU work was required.

Both local installations now match all 162 staged files, with independent application and data backups. Only the
core DLL and symbols changed; the other 160 files, including the model connector and runtime binding, remained
byte-identical. Accounts, approved briefs, existing work, audit chains and the exact two-occurrence schedule are
preserved. Read-only requests against both installed apps confirm the new methods and compact query previews;
those company-only queries returned no matching live records, so the positive retrieval evidence remains the
fictional regressions and staged HTTP test. Disposable staging was removed after the installers completed.

Broader relevance, paraphrase retrieval and customer usefulness remain open. These results establish a specific
context-coverage correction without claiming a new model-quality result.
