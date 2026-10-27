# 0089: Import Word body text into the existing document review flow

Date: 2026-10-26

Status: Accepted; bounded extraction, owner authority and fictional desktop/mobile journeys verified locally.

## Context

Private brand and project documents are part of the context-first proposal, but the document editor accepted
only pasted text and UTF-8 text/Markdown files. Requiring an owner to export every Word document adds avoidable
friction. Importing a formatted file also risks implying that its complete visible content has been preserved.

## Decision

- Add a role-independent documents integration using the platform ZIP and XML readers, with no new package.
  Read standard and Strict Word `.docx` packages in memory. Resolve the main body through the package's internal
  relationship and content type. Extract body text, paragraph/line breaks and tables with tab-separated cells.
  Never launch Office, extract files to disk, resolve XML entities, follow links or refresh field instructions.
- Bound compressed input to 2 MiB, package parts to 256, declared expansion to 16 MiB, body XML to 1 MiB,
  relationship/content-type XML to 128 KiB, XML depth to 64 and extracted UTF-8 text to the existing 64 KiB limit.
  Refuse malformed or ambiguous packages, unsafe part paths, external main parts, macro-enabled documents,
  standard embedded-object/ActiveX parts, tracked changes, embedded document imports and unreadable/empty text.
  No partial text is accepted after a failed extraction.
- Expose an owner-only, company-authorized extraction route with a separate 3 MiB request limit. Return the
  preview with `no-store`; extraction does not save a document, grant authority or invoke a model. Existing
  authentication, same-origin checks and company role checks apply. Other request limits remain unchanged.
- Let the owner inspect and edit the extracted text, choose its audience, save a private draft and separately
  approve the exact saved document. Retain the existing tenant isolation, digests, scoped retrieval, source
  revocation and document retention/deletion behavior. Store only the reviewed text and source filename.
  The original Word binary is not retained; the filename is a label, not proof of original binary provenance.
- Preserve newer edits, unfinished drafts and their audience across imports and reloads. A failed import leaves
  the editor unchanged. Disable saving while extraction is pending; do not replay a failed import automatically.
  Existing uncertain-save receipt recovery remains unchanged.
- Show the source filename in the editor after the picker resets. Keep a short body-text-only notice visible
  in both the editor and saved review. Expandable details explain omitted headers, footers, comments, notes,
  images, text boxes, formatting and automatic list labels, plus unrefreshed field values. The owner must
  correct missing context before approval. This is a text importer, not a full-fidelity Word renderer.

PDF, OCR, older `.doc`, macro-enabled formats, private connector ingestion and original-file storage remain
separate work. No runtime pin, schema, payment configuration or model execution changes are needed.
This extends [ADR 0056](0056-private-documents-and-retrieval.md).

Verification is recorded in [the Word-import receipt](../history/2026-10-26-reviewed-word-text-import.md).
