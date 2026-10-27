# Reviewed PDF text import — 2026-10-26

This turn made **verified progress** on private company knowledge. PDF uploads now extract selectable text into
an editable draft, retain numbered page boundaries, and mark pages without selectable text. Employees receive
only separately approved text within the chosen audience. The PDF binary is not retained. See
[ADR 0098](../adr/0098-reviewed-pdf-text-import.md).

The real pinned parser runs in a short-lived process with bounded input/output, concurrency, heap, observed
process memory and time. Its own deadline also stops it when the parent leaves stdin unfinished. This is not an
OS sandbox claim. Editor and saved review explain missing visual content, reading-order limitations and OCR.
File-format guidance is expandable so it occupies less space in the mobile editor. Failed imports and late
responses preserve newer edits; lost saves recover their original operation receipt.

## Verification

- Core, Documents integration, Host, Company Tests and Integrations Tests builds passed without warnings/errors.
  TypeScript/Vite passed with the existing large-chunk advisory. More than 10 GiB remained free before builds and
  the disposable package check.
- **28 backend facts passed**, zero failures/skips: 21 across `CompanyPdfImportTests`, `CompanyWordImportTests` and
  `CompanyDocumentTests`, plus seven existing `WordTextImportTests`. Six new PDF facts cover page labels, explicit
  textless pages, encrypted/scanned/damaged files, file/page/text limits, parser-message redaction, cancelled-slot
  recovery, the worker's real twelve-second self-expiry, owner/company isolation and document lifecycle.
- **1 company UI contract check passed**.
- **2 local Chrome desktop/mobile journeys passed**: new PDF import and existing Word import. The PDF journey
  uses actual fixture PDF bytes and the actual parser process. It checks mixed pages, text extraction, failed
  imports, interrupted HTTP, concurrent edits, reload/audience continuity, lost-save recovery, separate approval,
  scoped retrieval, revocation, visible filename/omission guidance and no horizontal mobile overflow.
- A local framework-dependent package successfully ran the PDF worker using its packaged DLLs and host
  dependency/runtime configuration. The fixture returned both pages. The child exited and the disposable package
  was removed after checking its resolved path; no deployment or upload occurred.
- A fictional source PDF was rendered and visually inspected. Desktop import, mobile editor and mobile approved
  review screenshots were inspected. C# / frontend formatting, JavaScript syntax and `git diff --check` passed.
- Initial checks exposed the core's old filename restriction; `.pdf` is now accepted as a text-source label.
  A copied Word-only browser assertion and notice wording were corrected before the final passing journey.

Evidence is retained in `test-results/pdf-backend.log`, `pdf-word-parser-regression.log`, `pdf-ui.log`,
`pdf-e2e.log`, `pdf-word-e2e-regression.log`, `pdf-import-browser.json` and `pdf-package-receipt.json`, plus compact
screenshots. Both browser runners waited for owned fixture processes to exit and removed disposable directories.
Fixtures contain only fictional material. The PdfPig dependency is pinned at 0.1.16; no agent runtime was upgraded.

## Limits

OCR, scanned text recognition, exact columns/tables/layout, image understanding, form values, signed-document
verification, original-file storage and connected-Drive PDF import are separate work. Unsupported extraction
never silently approves partial content. Actual customer documents and usefulness still require field validation.
No live model call, external read, payment, customer outreach or production deployment was performed.
The complete three-phase goal remains active.
