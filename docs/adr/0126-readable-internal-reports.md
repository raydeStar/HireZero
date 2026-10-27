# 0126 — Readable internal reports

Status: accepted, 2026-10-26.

A real Marketing result exposed Markdown syntax as plain text in the report panel. Internal reports now use the
already-pinned Markdown renderer for headings, emphasis, lists and code blocks. Raw HTML and images are disabled;
rendering a report must not automatically retrieve external content. Links retain the renderer's URL filtering and
open with opener isolation. The separate exact-call JSON view remains plain text.

Rendering does not rewrite the stored artifact, its digest, provenance or approval state. Long reports retain the
expand/collapse control. The real saved result was checked after browser refresh, with owner approval still pending.
