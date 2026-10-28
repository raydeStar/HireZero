# 0145 - Open deliverables in focused Work review

Status: accepted, 2026-10-27.

Opening a saved recurring result inherited the current Chat mode. At a typical desktop width, the idle composer
then occupied more space than the draft itself. Model-selection help also forced horizontal overflow inside the
split composer. This made useful work harder to read and correct.

Opening a non-conversation task now defaults to Work mode, using the full main panel for its existing review.
The mode remains in the address, so refresh retains it. Conversation replies still open in Chat. Explicit Chat
mode and older links preserve the two-panel layout described by ADR 0040; **Show in chat** remains available on
wide screens, and the split work header now offers **Expand work** to return to focused review. Expansion moves
keyboard focus to the work window.

The composer uses a shrinkable grid column. Its model/account label and explanatory text wrap within the
available width. Existing selected models, message drafts, owner edits, approvals and execution behavior are
unchanged. Switching between full and split review uses the existing draft-recovery storage, rather than
duplicating editors or dispatching work.
