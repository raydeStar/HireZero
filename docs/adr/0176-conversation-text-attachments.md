# 0176 — Conversation attachments are local text references

Status: accepted, 2026-10-31.

The owner wants screenshot feedback and documents in chat, with image generation deferred because its costs are unresolved.

Attachments enter a private employee conversation as bounded, editable extracted text, separate from the owner's message and approved knowledge. They are stored with that message and included in its sealed review packet. Historical attachments follow the same thread boundaries and history limits. They never approve facts, grant tools or trigger a model call.

The Documents integration reads PNG/JPEG text with Windows OCR in a bounded child process (two slots, 15-second deadline, 384 MiB process limit, 16 million pixels, 2 MiB input). Its trusted embedded script receives bytes through stdin; originals are neither persisted nor passed to a model. Existing Word and selectable-PDF readers are reused. Three attachments may supply at most 6,000 UTF-8 bytes of selected text; excess content requires explicit selection rather than silent truncation. The existing whole-packet budget still applies.

This is text recognition, not visual understanding. The interface and model instructions state that limitation. No cloud fallback, new package, model permission, generation capability or schedule-policy change is introduced. Unsupported systems and unavailable OCR return an actionable error. Sharing durable company knowledge remains a separate reviewed action.
