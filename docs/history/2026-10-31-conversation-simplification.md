# Simpler conversations and local text attachments — 2026-10-31

The owner rejected the composer clutter and requested screenshot/document feedback. Image generation remains deferred.

## Delivered

- Message box and primary action stay visible. Attach and Options form one quiet footer. Work type, model, allowance, save-only mode and approved evidence are under Options; employee tools are collapsed. Invalid settings reopen Options so browser validation cannot strand the user behind a hidden field.
- Prepared replies load their exact review automatically. No second review-loading click is required. Authorization remains explicit, single-use and digest-bound; uncertain outcomes still cannot replay.
- Attach PNG/JPEG screenshots or paste a screenshot into the message field. Windows OCR returns editable text locally. PDF with selectable text, Word .docx, UTF-8 text and Markdown can enter through the same Attach control. Original files and images are not retained or passed to the model.
- Attachments are visible with their saved message and in its sealed review. They remain private conversation reference material, separate from the owner's request and approved company knowledge. Three files may supply 6,000 UTF-8 bytes of selected text; users select passages when larger. The overall packet limit remains unchanged.
- The architectural boundary is recorded in [ADR 0176](../adr/0176-conversation-text-attachments.md). No package or runtime pin changed.

## Verification

- Web production build and .NET build pass. Six new attachment/OCR facts pass, covering real local OCR on a fictional image, whole-message rejection, unchanged reference text, thread separation, sealed context, file limits, owner authorization, cross-company refusal and compatibility with pre-upgrade command recovery receipts.
- Eight focused existing document/schedule regression facts pass. The measured UI test passes, including automatic review loading and fresh authorization.
- Five isolated Chrome journeys pass: conversation replies, action recovery, company chat, employee review and support walkthrough. Conversation coverage includes pasted screenshot OCR, correction/removal, text attachments, transcript persistence, no model dispatch on import, desktop/mobile disclosure, cancellation and no replay. These use a fake provider; there were zero live model calls.
- Both local installs were backed up and updated. Each matches all 165 staged files. Existing work, model connections and audit chains are preserved. Both active finite schedules still have current reviews; automatic coding, sandbox/browser execution and general model execution remain disabled.
- Installed-app walkthrough confirms Attach opens the picker and a fictional screenshot produces editable text. The temporary attachment was removed without saving a message or dispatching a request. Private screenshots, logs, backups and install receipts remain outside Git.

OCR reads words, not layout, colors, objects or diagrams. Scanned PDF pages still need a screenshot crop or a text copy. OCR quality depends on the image and installed Windows recognition languages. Answering about extracted text uses the normal separately authorized model request. No native model usefulness acceptance was claimed for this cycle.
