# Copy individual draft sections

Copying a weekly plan previously required extracting each post from the complete document. Drafts now show
a small **Copy section** button beside Markdown headings and standalone bold section titles. The button
copies the original Markdown between that title and the next title, excluding the title and surrounding
separator lines. It does not rewrite text or create an artifact. Whole-draft copy/download and collapsed
internal notes remain available. Unstructured drafts retain the existing whole-draft controls.

This presentation change is shared by roles. It does not recognize weekdays, require exact labels, change
model instructions or promote a result into company knowledge. Section buttons have accessible names
that include their titles and report clipboard failures locally.

## Verification

- Formatting, TypeScript/Vite build and whitespace checks passed. The existing bundle-size warning remains.
- Local fixtures checked natural and bold titles, nested headings, CRLF, lists, tables, quoted/fenced heading
  text and drafts without headings. Copies are exact source slices.
- Installed UI verification caught the bold-title variant; it now exposes separate post controls. A real
  clipboard read matched the selected saved post exactly, excluding neighboring posts and internal notes.
  Whole-draft copy still returned the complete usable draft without review notes. The normal narrow app
  viewport was visually inspected; a screenshot is retained privately.
- Both local installs were updated with application/data backups. Installed-file checks passed; existing
  work, accounts, schedules and audit chains were preserved. All 146 server files match the pre-update app.

No model calls, publishing, artifact edits or schedule activation occurred in this cycle. This removes a
copying chore; it supplies no new evidence of model usefulness or owner acceptance. Exact receipts and
screenshots remain in the ignored local pilot directory.
