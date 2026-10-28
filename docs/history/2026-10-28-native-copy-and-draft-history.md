# Native copy verification and compact draft history

The unresolved clipboard check now has a native observation. In the installed pilot, clicking **Copy draft**
on the approved result displayed success. Pasting into a new, empty Windows Notepad tab produced the complete
accepted draft. Its 3,238 characters and SHA-256 fingerprint exactly matched the saved artifact after newline
normalization. The browser automation clipboard reader simultaneously returned an empty string. That reader
therefore does not establish the contents of the Windows clipboard in this session. The original clipboard
implementation remains unchanged; the temporary Notepad text was cleared and its tab closed without saving.
Unrelated restored tabs were left unmodified. This verifies this native path, not every browser or clipboard.

The same walkthrough exposed repeated full-length drafts below the accepted result. Under
[ADR 0152](../adr/0152-collapse-earlier-draft-bodies.md), the first result opens fully and earlier copies have
collapsed bodies. Each disclosure retains its stable draft number, authorship and review state. Earlier text
and its controls are available on expansion. No stored artifact, approval, model routing or recurring authority
changes.

TypeScript/Vite and the release build pass. The fictional first-plan/edit/revision browser journey passes,
including refresh, expansion of an earlier draft, stable numbering, preservation of the original and model
records, approved-first completed work and the narrow-screen review. The existing bundle-size advisory remains.

Both local workspaces have the tested update, with separate data backups and prior application files retained.
All 180 release files match in each installation. Accounts, saved work, model records, schedules and both audit
chains are unchanged. The installed pilot opens the accepted third draft with the other two collapsed; reopening
and closing an earlier copy works. The accepted plan and exact active two-occurrence weekly schedule remain intact.

No live provider call or runtime qualification job was needed. The broader customer, recurring-use, commercial
and production-runtime acceptance gates remain open.
