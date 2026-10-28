# Action recovery across reload and navigation

Status: implemented, browser-verified and installed in both local workspaces with recovery backups.

The next scope audit found employee actions bypassing the shared durable command helper. Their server request
IDs were idempotent, but the UI held them only in memory. Reload after a lost response could therefore offer a
new action with a new identity. [ADR 0148](../adr/0148-action-receipts-survive-navigation.md) records the fix.

The client checks cover receipt restoration, company/member/conversation separation, failed storage, missing
receipts, mismatched action/task identity and read-only recovery of the current task. Production TypeScript/Vite
compilation passes with the existing bundle-size advisory. No backend schema or runtime pin changed.

The fictional browser journey retains its confirmed-save/failed-refresh cases and adds storage refusal before
dispatch, a genuinely executed arithmetic action whose reply is lost, failed receipt reads, reload restoration,
and recovery without a second action. A navigation race holds an original response, recovers its saved task in a
new form, clears that receipt explicitly, then releases the old response; the old form cannot restore it.
Mobile layout is checked separately. All inputs are fictional and the arithmetic tool makes no model call.

The complete browser journey passes with exactly three fictional arithmetic actions and zero provider starts.
Both focused client checks pass. Both installed workspaces are healthy, with all 181 release files verified,
intact audit chains and unchanged accounts, saved work and schedules. The accepted Marketing plan and the
exact active two-run schedule remain intact; specialist originals and their separate editorial copy remain
available for owner review. No native model, Jev or runtime qualification job was needed for this change.

## Jev finding and remaining work

The current Jev adapter uses a machine-level TypeSafe key; the separate company-scoped OpenRouter adapter
currently uses chat completions. They are different from a company-scoped Jev Decisions connector.
OpenRouter documents the [Decisions API](https://openrouter.ai/docs/api/api-reference/alphadecisions/submit-a-decisions-request)
for typed answers and reported usage. Its [Jev guide](https://openrouter.ai/docs/guides/community/jev) distinguishes
that API from Jev Router, which chooses a text model and effort. Advisory classification should not silently
replace an owner's chosen text model/account or authorize execution.

The missing connector needs an exact decision/model/input review, company-bound credentials, metered consent
and durable uncertain-outcome accounting. Existing ordinary employee actions are not a substitute for that
metered path. No credentials were changed, no Jev/OpenRouter request was made, and Luna/Codex OAuth routing
and the accepted Marketing schedule were not changed by this work.
