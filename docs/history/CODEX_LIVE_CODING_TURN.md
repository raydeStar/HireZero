# Live coding turn (live step 2), 2026-10-18

You approved one real coding turn through the company workspace's live coding runner on a fictional repository with
low limits, and later confirmed that token cost was not a concern for testing.

## Setup

- **Runtime:** the pinned `codex-cli 0.163.0-alpha.10.2`, with your dedicated sign-in folder. Its path is not
  recorded here.
- **Runner:** `CompanyCodexCodingFactory` and `CompanyCodexCodingRunner` exactly as the host would use them.
  - **Preflight:** account, model catalog, effective sandbox profile and thread isolation.
  - **Sandbox:** `hirezero_execution`, with the checkout and its scratch folder writable and network off.
- **Repository:** a fictional git repository with one file and the task: fix the typo "Teh" to "The" in `notes.txt`,
  change nothing else, and don't commit.
- **Limits:** low effort, 5 minutes. Tokens: 20,000 for the first two attempts, then 100,000.

## Attempts

| Attempt | Outcome |
| --- | --- |
| 1 | Completed, but the model replied that it had no file access: 8,774 tokens, no change |
| 2 | Completed, but the model replied that its tool host was disabled: 15,944 tokens, no change |
| 3 | Completed in 14 seconds: the model ran two commands in the sandbox, changed exactly "Teh" to "The", confirmed the diff and did not commit (16,930 tokens) |

In every attempt the safety properties held:

- preflight passed;
- the run state, model (`gpt-6.1-sol`) and usage were reported;
- the process was confirmed stopped;
- the repository's HEAD was unchanged, with no commit;
- the run's scratch folder was removed.

## Fixes from this check

- **Environments.** The runner sent an empty `environments` list on the thread and turn, a setting copied from the
  text-only connection. In 0.163, an empty list disables environment access, so the model had no tools. Coding now
  omits it (the default environment) and sets the workspace root to exactly the approved checkout.
- **Tool host.** Coding disabled `code_mode_host`, which in 0.163 hosts the model's tools. Coding now keeps it; text
  replies still disable it.
- **Tests.** FakeCodex now refuses a coding launch that sends environments, uses any workspace root other than the
  checkout, or disables the tool host.

## Notes

- The edit came through command executions, not a `fileChange` item, so the run's events show commands only. The
  diff review is what confirms the change.
- With limits passed in, the fix-run defaults (200,000 tokens, 15 minutes, medium effort) are unchanged.
- The next live step is a real `pr-fix` on a branch of yours. Its push is a separate approval in the app.
