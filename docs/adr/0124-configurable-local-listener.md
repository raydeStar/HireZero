# 0124 — Configurable local workspace listener

Status: accepted, 2026-10-26.

An installed workspace can already occupy the default local port while a newer checkout needs an acceptance
walkthrough. Local mode previously ignored the typed `HIREZERO_LISTEN` setting, preventing a separate preview.

Local company mode now honors that setting after any explicit test-fixture address. The default remains
`http://127.0.0.1:5065`. The existing HTTP loopback-IP validation, exact host/origin checks and Kestrel listener
restriction still apply; a configured network-wide listener is refused before creating company storage.
Hosted mode is unchanged. An independent preview uses a separate data root and port, preserving the installed
workspace. This does not deploy the new build or authorize model execution.
