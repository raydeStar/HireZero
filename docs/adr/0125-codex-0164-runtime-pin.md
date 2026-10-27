# 0125 — Review the installed Codex 0.164 runtime

Status: accepted pin update, 2026-10-26. Live text execution and coding qualification remain separate evidence.

The previously pinned 0.163 executable is absent from the inspected current installations. The official desktop
bundle now contains `codex-cli 0.164.0-alpha.11.1`. Its executable hash and source bundle are recorded in
`runtime/codex/pin.json`; no runtime was downloaded, replaced or upgraded in place. This standalone executable
has no npm dependency lockfile to regenerate.

The existing read-only app-server inspection successfully checked ChatGPT authentication, included-usage
permission and the selected text model's catalog entry using a dedicated sign-in home. It sent no model turn.
This establishes connection metadata only, not successful model output or coding sandbox qualification.

Codex text execution retains an exact bundle identity, one-request authorization, strict text-only configuration,
explicit task context, local deadline and measured usage stops. Included ChatGPT usage is distinct from an API
key's monetary allowance; neither a hard per-job dollar cap nor an API fallback is implied. Offline adapter
regressions and the actual selected workflow must be checked before claiming live acceptance. Coding remains
disabled for this acceptance case and needs its own qualification before activation.

Subsequent evidence: the two focused offline adapter/queue facts passed, and one owner-authorized text-only Luna
assignment completed through the real application using this pin and dedicated OAuth profile. Its output and token
receipt persisted and were inspected after browser refresh. This proves that text path; it does not qualify coding.
