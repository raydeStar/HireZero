# Delegated script journey — 2026-10-26

Scope: compose approved plans, selected predecessor evidence, bounded scripts and reviewed task results under
[ADR 0083](../adr/0083-reviewed-scripts-in-delegated-plans.md). This advances P3.2/P3.3 of the context-first
product track. It does not qualify or activate a production runtime.

Ordinary plan assignments can now prepare scripts after their predecessor work is completed and approved.
Owners select exact approved task results and/or approved documents; only those selected bodies become input
files. The script captures its parent plan and predecessor revisions, requires separate one-time consent,
uses shared compute allowance and leaves the parent's model-token ceiling unchanged. A reviewed output enters
the ordinary task-completion flow and can complete the parent plan.

New execution requires current, unexpired authority. Saved results remain reviewable after parent completion
and the execution deadline, while source revocation still prevents reuse. Model and script admission exclude
competing work on the same task, including scripts awaiting confirmed resource release. Revoked late output
is withheld without silently releasing an uncertain resource hold or retrying execution.

Browser testing found and fixed three UX defects:

- A task-version conflict stranded the script draft against stale state. The UI now refreshes the task, preserves
  the draft and explains that explicit resubmission is needed.
- Duplicate React keys left several obsolete execution panels visible after completion. Measured, script and
  browser components now have separate keys; the browser check asserts no stale model-execution cards remain.
- Plan work was titled only "Conversation". Work headers and the cockpit now show the assignment's first line.
  Completed plan work shows saved-history copy instead of telling the owner to finish predecessors.

Verification:

- **17 company tests passed** across delegation and sandbox hosting, including eight new focused lifecycle
  facts. They cover selected evidence, private-context exclusion, parent completion, unchanged token limits,
  persisted history, competing admission, wrong-company/unrelated artifacts, expired/canceled plans, changed
  source proposals/revisions, revoked measured context, and late-output withholding with retained allocation.
- **44 integration tests passed** across sandbox, browser and browser-retention regressions.
- **Three existing company regression groups passed**: Inline, MeasuredQueue and TaskHandoff. The latter two
  initially hit sandbox loopback restrictions, then passed when run with local network permission.
- The new **Chrome plan-script journey passed** through the real host/API/store/UI with fictional providers.
  It selects an approved predecessor result, preserves a draft through an observed revision conflict, reviews
  exact inputs, dispatches once, reviews the output, completes the parent, recovers the fixture's uncertain
  cleanup, reloads completed history and rejects reuse after source revocation. It checks assignment titles,
  stale execution cards and 390px overflow. Desktop and mobile screenshots were inspected.
- The existing **standalone script Chrome journey passed**, covering output review, handoff, source revocation,
  recovery and runtime-disable behavior.
- Company UI recovery checks passed. Core, host and both affected test-project builds passed; frontend
  typecheck/build passed. The existing main-bundle size warning remains. Seven affected C# files passed
  CSharpier checks; frontend formatting and whitespace checks passed.

Compact logs, a JSON receipt and desktop/mobile screenshots are under ignored `test-results/plan-script-*`.
The fixtures stop their own processes before removing temporary profiles/data. Free disk space was above
315 GiB. Existing dirty work and unrelated running applications were preserved.

The script provider is fictional: **no JavaScript was executed in a real container**, and no live model,
payment or external account was contacted. Real containment qualification, production activation, browser-plan
and recurring-script composition, repository orchestration and customer usefulness remain pending. No commits,
pushes, deployments or charges were made. The full three-phase product goal remains active.
