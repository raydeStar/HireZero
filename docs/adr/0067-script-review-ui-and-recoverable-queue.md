# 0067: Script review UI and recoverable host queue

Status: Accepted. Verified with a fictional executor; real runtime registration remains pending qualification.

## Decision

Compose [0066](0066-durable-sandbox-authority-and-allocation.md) through a single bounded host worker and
advanced script tools inside an existing employee task. The task remains the navigation and provenance anchor.
Usage adds an independent script allowance, explicit consent to policy changes, recent records and task links.

An owner selects approved documents in the task's audience, supplies JavaScript and selected output names,
and prepares an exact review. No script runs during preparation. The editor waits for its input list, shows
the eight-file/128-KiB selection bound, and moves focus to the saved review after preparation. The review
contains actual script/input text, selected outputs, runtime and limits. A separate consent checkbox enables
one-run approval only when the current allowance permits it. Company members without owner authority can
read recorded work but cannot prepare, approve, stop or recover it.

The generic command route retains its small request ceiling. A dedicated owner-only proposal route accepts
up to 512 KiB of serialized JSON, while Core still enforces the 32-KiB script and selected-input bounds. The
larger transport envelope accounts for JSON escaping; it does not expand execution authority.

Approval commits a queue entry and reservation, then returns its operation receipt. The background worker
claims it exactly once. Only one script runner uses this host's worker slot at a time. Startup snapshots all
companies' pre-existing unsettled jobs before requests are accepted and recovers those entries without
replaying them. New approvals received after startup are ordinary queue work. Manual recovery uses the same
worker slot and the selected company's store. No provider action crosses a company boundary through a job ID.

The proposal's one-hour review expiry also bounds its wait for a worker. The shorter execution deadline starts
when the durable dispatch is claimed, so waiting in the queue does not consume the script's execution window.
The approved script-seconds allocation remains held throughout the queue and release lifecycle.

**Revocation removes authority and accepts an older positive revision of the same immutable job.** Browser
testing found that requiring the latest progress revision could reject Stop immediately after a worker claim.
Revocation cannot expand scope or authorize a replacement job, so newer progress must not defeat it. Exact
revision/digest checks still apply to approval; future/nonexistent revocation revisions remain invalid.

Saved output and resource release are presented separately. Cleanup failure retains the output and shows the
held allocation. Recovery stays available while execution is paused. Unknown jobs have no automatic rerun
control. Revoked late output is withheld. Ordinary tasks without an enabled runtime or saved script history
do not fetch or display the advanced script panel. A disabled provider does not hide previously saved work.

## Verification and activation boundary

The production CLI does not yet register the Docker executor. The host accepts a fixture executor only with
its existing test-listener parameter. The compiled fictional browser fixture never evaluates JavaScript or
provisions resources. It exercises the real routes, store, scheduler, notifications and React interface.

Desktop/mobile browser evidence covers explicit policy consent, exact review, selected inputs, response-loss
reconciliation without duplicate execution, cleanup failure, retained output, recovery while paused, stopping
running work and withheld late output. Focus moves to the prepared review; the 390-pixel view has no horizontal
overflow. Existing AI Usage browser checks still pass. Separate HTTP tests cover a 20-KiB script, repeated
approval, company isolation and startup recovery of companies that nobody has opened.

Real runtime configuration, pinned-image qualification, artifact promotion, scheduler/plan composition,
compute pricing and premium execution remain separate requirements. This change is not proof of live containment.
