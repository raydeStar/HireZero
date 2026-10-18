# Live pull-request fix (live step 3), 2026-10-18

You approved a real pr-fix through the company workspace on one of your own dependency pull requests, with network
access for that fix only, and kept the push as a separate approval. The repository and pull request are not named
here.

## Setup

- **Host:** the company workspace with `--company-codex --company-coding`, on the pinned `codex-cli 0.163.0-alpha.10.2`
  with your dedicated sign-in folder.
- **Flow:** the owner flow through the HTTP API:
  1. gather and approve pull-request evidence;
  2. create the pr-fix task;
  3. grant the checkout with network on;
  4. prepare, approve and run.
- **Task:** CI failed with NU1004 (a launcher project's lock file was missing a runtime section). The brief asked for
  the lock file to be regenerated through a restore, with nothing else changed and no commit.
- **Limits:** 200,000 tokens, 15 minutes, medium effort, network on.

## Runs

| Run | Outcome | Defect found and fixed |
| --- | --- | --- |
| 1 | Dispatched; the review after the run exceeded its budget, so the outcome was recorded as unknown and not replayed | The machine's Git config converts line endings on checkout, and the byte-exact review counted every file as changed. Prepare now reviews the untouched checkout and refuses one that already differs, before anything is sent. |
| 2 (prepare) | Refused before dispatch, with nothing sent | The repository's own `.gitattributes` forces CRLF. The review now treats Git's exact CRLF checkout conversion (stray CRs kept) as unchanged. Only reviewed paths are staged, so such a file is never committed. |
| 3 | Completed with no change (153k tokens); NuGet could not start | NuGet needs `ProgramFiles(x86)` and `ProgramData`. The runner now passes those machine locations, plus `ComSpec` and `PATHEXT`. |
| 4 | Completed with no change (152k tokens); restored offline only, and the review listed `obj/` output | Ignored untracked paths are now left out of the fingerprint and the review, and the prompt states the run's network access. |
| 5 | Completed with no change (129k tokens); still restored offline | A probe with no model call showed the sandbox reaches the NuGet index over HTTPS when network is on. The prompt now says plainly that packages may be restored. |
| 6 | Completed with no change (155k tokens); still "no network" | The runner's fixed system instructions banned network and package installs, overriding the task. They now follow the grant. |
| 7 | Completed (167k tokens): one file changed, `packages.lock.json`, with a 324-byte patch adding the missing runtime section; a locked restore for that runtime passed | none |

In every run:

- the grant's network choice matched the profile, the effective configuration and the thread;
- HEAD was unchanged, with no commit;
- usage stayed within the grant;
- the process was confirmed stopped;
- nothing was published.

## Verdict on the fix

The agent's change is mechanically correct but not the right fix. The project declares one runtime identifier on
Windows and two on the Linux CI, so a lock file listing both breaks a locked restore on Windows; the agent reported
that itself. The base branch had already fixed the CI job, so the pull request needs updating from its base branch, not
a lock-file commit. No publication was proposed, and nothing was pushed.

## Follow-ups

- When a run fails after dispatch, its report is lost: the run's result lives only in the runner's memory. It should be
  kept with the unknown outcome so the owner can see what the agent did.
- An evidence summary that compares the pull request's branch with its base would have flagged "already fixed on the
  base branch" before any run.
