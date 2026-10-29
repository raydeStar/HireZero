# Combined handoff verification

Resumed from `2983e22`, preserving the weekly-plan rubric changes, versioned output policy,
per-model API reasoning settings and queued database writers. This cycle checks the combined
application and installs the current code; it adds no prompt iteration or architectural decision.

## Regression results

The five .NET suites contain 895 tests. The initial run had 883 passes, one failure and eleven
environment-dependent skips. The failure was the existing Core architecture check: a new comment
in `ModelRoutes.cs` named an integration provider. The comment now describes the generic API
reasoning setting. No runtime behavior or test assertion changed. All thirteen Host tests passed
on rerun, giving 884 passes and eleven skips across the verified suites.

| Suite | Passed | Skipped |
| --- | --- | --- |
| Core | 43 | 0 |
| Host, after the comment correction | 13 | 0 |
| Policy | 1 | 0 |
| Integrations | 215 | 6 |
| Company | 612 | 5 |

The solution build and release publish passed. The earlier interrupted build had completed;
the fresh verification rebuilt the later handoff before testing. A previous parallel solution-test
launcher failed in MSBuild before test execution, so this run built with one worker and ran each
test assembly directly. This was a local runner failure, not a failed product assertion.

The fictional Home browser walkthrough passed: ordinary website addresses, bounded optional
discovery, automatic employee setup, single Home consent and dispatch, lost-reply/reload recovery,
the exact new result opening, and desktop/mobile layouts. The requested welcome email stayed an
email rather than becoming a weekly plan. It made zero live model calls and zero external requests.
Repository formatters passed without unrelated source changes.

## Local installation

Both local installations now run the combined build, including the latest weekly-plan instructions
and the scheduler lock fixes. Each update retained a complete data backup and the original binaries;
all 181 staged files matched their installed copies. The temporary release stage was removed after
verification, while backups and compact receipts were retained privately.

Before/after comparisons covered all fifteen companies: thirteen in the primary installation and
two in the pilot. Saved employees, conversations, tasks, artifacts, knowledge, measured receipts and
schedules were preserved, and every company audit chain verified. Both hosts are healthy. Automatic
model, coding, sandbox and browser execution remain disabled.

Home still discloses Luna through Codex OAuth, confirmed high effort, v3/6,000 and the 90-second
local deadline. The existing pilot schedule retains v2/3,000 and its outdated approval. It was not
upgraded or re-approved. Existing raw model outputs were not edited or replaced.

## Product interpretation

The rubric work is preserved as [comparison evidence](2026-10-29-weekly-plan-rubric-iteration.md),
including its weaker samples, grader variation and diminishing returns. Grades on two repeatedly
used businesses do not establish useful first drafts across the target market. The owner's earlier
photography assessment is recorded there; it is no longer awaiting an answer.

The full proposal still has separate engineering, live-service and customer-validation work. In
particular, hosted onboarding and real connector acceptance, complete retention/deletion,
retrieval relevance, actual recurring usefulness, complete cost inputs and commercial validation
remain open in the evidence map. Passing this regression check does not close those requirements.

The next quality check should use fresh businesses and untouched first outputs, assessing practical
usefulness before any revisions. Further changes should address observed failures rather than chase
small grade changes on the same examples. This cycle made no new model requests, changed no default
model, and performed no publishing or outreach. The original 31-run evaluation allowance is already
consumed; later authorizations and their usage remain recorded in the rubric history and private receipts.
