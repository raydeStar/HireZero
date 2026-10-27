# 0098: Review selectable PDF text before adding private company knowledge

Date: 2026-10-26

Status: Accepted; local parser, owner authority and desktop/mobile verification. OCR and visual fidelity are not provided.

## Context

Private brand guidelines and client briefs are part of the context-first proposal. The existing private-document
flow accepts text, Markdown and Word, but rejects PDF. Requiring a manual export for every PDF adds onboarding
friction. PDF extraction also needs clear limits: selectable text is not a faithful reconstruction of a page.

## Decision

- Pin [PdfPig 0.1.16](https://www.nuget.org/packages/PdfPig/0.1.16) in the role-independent documents integration,
  with its package hash in affected lockfiles. Record its Apache-2.0 metadata in third-party notices. Do not alter
  an agent runtime pin. The documents assembly also provides a small `--extract-pdf` process entry point.
- Parse uploaded bytes in a short-lived process using the host's installed .NET runtime/dependency configuration.
  Clear inherited environment settings except the runtime/system essentials. Pass document bytes through stdin
  and a bounded JSON result through stdout; never save the input binary or expose parser exception text.
  No browser, shell, PDF actions, scripts, links, attachments or external services are invoked by extraction.
- Limit uploads to 2 MiB, documents to 50 pages, nesting to 64 and final UTF-8 text to 64 KiB. Use strict parsing
  and reject unreadable text rather than silently returning a partial result. Reject encrypted PDFs, including
  files that could be opened with an empty password; no password entry or bypass is offered.
- Admit at most two simultaneous parsers per host. Set a 128 MiB managed-heap limit, terminate when observed
  private memory exceeds 256 MiB, cap response/stderr reads, and impose a ten-second parent deadline. The worker
  separately exits after twelve seconds even if its parent stalls. Cancellation kills the process tree and waits
  for exit. These controls protect host responsiveness; they are not an OS/container sandbox or a proof against
  all parser vulnerabilities, and sampled process memory can overshoot its threshold.
- Extract selectable page content, retaining numbered page boundaries. Mark every page without selectable text
  in the draft and report those page numbers. Reject wholly textless/scanned PDFs with OCR/export guidance.
  Tables and reading order may differ; images, annotations, attachments and interactive form fields are omitted.
- Add an owner-only, company-authorized extraction endpoint with a 3 MiB request limit, `no-store` responses and
  a fresh owner check after asynchronous parsing. The endpoint is a stateless preview, not a saved record or an
  approval. Store only edited text and a source filename through the existing document commands.
- Reuse audience selection, exact saved-text approval, isolated retrieval, revocation, draft retention and
  deletion. Preserve unfinished edits and audience across reloads, failed imports and late responses. Require
  separate approval before employee use; a filename is a source label, not proof of the original file's contents.
- Explain extraction limits in both editor and saved review, including missing-page markers and the absence of
  OCR. Existing Word and UTF-8 imports retain their behavior.

## Consequences

Selectable-text PDFs can enter the existing private knowledge workflow with a visible review step. Scanned text
requires an owner-reviewed OCR/text export. Layout, charts, complex reading order, signed-document authenticity,
original-file storage and PDF imports from connected Drive remain separate work. The worker needs the installed
framework-dependent .NET host; a missing runtime fails with text-import guidance. No schema migration, model
execution, payment or production runtime activation is introduced.

See the [verification receipt](../history/2026-10-26-reviewed-pdf-text-import.md).
