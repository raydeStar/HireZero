# 0083: Reviewed scripts in delegated plans

Date: 2026-10-26

Status: Accepted; fictional lifecycle and product verification. Production runtime qualification remains separate.

## Context

Measured plan tasks can share approved predecessor results, while isolated script tools previously refused every
plan task. This prevented a coordinator's analytical assignment from moving through selected evidence, a bounded
calculation and a reviewed result within the same plan.

## Decision

- Script tools may attach to ordinary work tasks in an approved executing plan. Preparing a script requires an
  active parent within its execution window and completed predecessors with approved current results. Meetings
  and scheduled occurrences retain their separate contracts.
- Each script captures the parent plan ID/digest and the exact approved predecessor artifact revisions/digests.
  Source-proposal changes, dependency revocation, source-context revocation, cancellation and task/employee
  generation changes invalidate execution and later reuse. New execution requires the live plan deadline;
  completed results remain reviewable after that deadline when their source authority remains current.
- Owners can explicitly select task evidence and approved predecessor results as UTF-8 input files alongside
  approved documents. Only selected bodies enter the runtime. Employee conversations, private notes and other
  company artifacts are not implicitly copied. The existing combined eight-file / 128 KiB input limit and exact
  filename/output constraints apply across both input categories.
- Plan approval does not authorize script execution. Each immutable script packet needs its existing exact,
  one-time owner consent. Shared monthly purchased and safety compute limits still apply. Script allocation
  neither spends from nor enlarges the plan's model-token ceiling.
- A task cannot start a model request while a script has unsettled resources, or start a script while a model
  request is active. Completing a task fences scripts prepared earlier. Uncertain execution is never replayed;
  invalidated late output is withheld while its resource/usage hold remains until confirmed release.
- A selected script output follows the ordinary artifact review and explicit task-completion flow. Completing
  the final child completes its parent plan. Output lineage remains revocable after plan completion and reload.
- The script form labels approved result inputs by employee and task. Exact review shows their content and
  revisions. A version conflict refreshes task state and keeps the unsent draft for explicit resubmission.
- Plan work uses its assignment title in the work window and cockpit. Execution components have distinct React
  keys so completion removes obsolete run controls. Completed assignments show saved history rather than
  instructions to finish predecessors.

The additional plan and evidence fields are optional on stored script records. Existing standalone records keep
their saved approval/digest and continue through their original lifecycle; no schema migration is required.
Browser-plan execution, recurring scripts, automatic repository changes, premium environments and production
container qualification remain separate work. This decision does not enable any live runtime by itself.

Verification: [delegated script journey](../history/2026-10-26-delegated-script-journey.md).

Extended by [ADR 0084](0084-reviewed-browsers-in-delegated-plans.md) for shared plan authority, browser assignments
and mutually exclusive model/script/browser admission on a task.
