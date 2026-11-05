# Luna xhigh in the existing company workspace

The owner requested local OAuth testing through the existing HireZero application. The original company installation now enables `HIREZERO_COMPANY_CODEX_XHIGH=1` and continues using its dedicated ChatGPT sign-in and pinned Codex runtime. The setting is described in [ADR 0178](../adr/0178-local-codex-xhigh.md).

All 16 original uncompressed frontend assets matched their pre-install SHA-256 hashes. Existing Home, employee Work, model review and receipt screens supplied the complete test workflow. A consistent private database backup and the previous application build were retained locally.

The first real Home request completed with Luna xhigh but followed an older business goal instead of the current one. The application instructions now identify `input` as the current assignment and distinguish saved goals as background. A fresh request through Home then returned the requested heading and one-sentence internal note, saved it to the correct task and displayed its usage receipt. Both attempts and their outcomes remain in the private test company.

The final call reported 6,167 input tokens and 344 output tokens, including 324 reasoning tokens, totaling 6,511. Its saved policy recorded `xhigh`, its route was `openai/gpt-6-luna`, and its account provider was `codex-oauth`. The earlier completed call reported 9,107 tokens. These were explicit live local checks in an existing fictional test company. Routine checks used fakes.

Verification covered 86 passing focused company tests, including Home preparation, native effort confirmation and refusal, stale approval rejection after an effort change, schedule policy preservation, model routing, settings, business drafts and memory analysis. Builds completed with zero warnings and errors. CSharpier, frontend formatting and diff checks passed. The test company's audit chain remained intact after the live run, and the owned native process exited.

OAuth usage remains measured after execution with local soft limits. Runtime qualification and remote deployment retain their existing evidence requirements.
