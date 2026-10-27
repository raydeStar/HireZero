# 0113 — Script image inventory and local qualification plan

Status: owner-approved local build and complete standard/extended run recorded; host task exhaustion is now
diagnosed, but a reliable fix remains unqualified. See the [execution evidence](../history/2026-10-26-local-script-qualification.md)
and [diagnosis](../history/2026-10-26-script-runtime-task-exhaustion.md). Application execution
remains disabled, and runtime reliability is not accepted.

The bounded JavaScript worker already has a digest-pinned recipe and opt-in standard/extended qualification
suite. The browser's local runsc pass does not qualify this different worker, its Alpine libraries, input/output
permissions or longer deadlines. Its existing Node base pin is retained; this is not a runtime upgrade.

Record the existing index, its Linux amd64 manifest and config digests, declared exact Node version and compressed
layer size in `runtime/sandbox/pin.json`. Select that architecture explicitly. Allowlist the build context and
add a build-time inventory that verifies the actual Node version, root ownership and protected paths, then
records executable/worker hashes. The inventory always says `qualified: false`; only a separately executed
qualification may issue a receipt. No floating package install or dependency download runs in the recipe.

Prepare the next local experiment on the already-installed dedicated qualification host. It builds this worker
and runs all standard cases plus the two extended cases, with their existing limits and fictional inputs.
The extended case is bounded batch work, not a persistent developer environment. A read-only saved-receipt
fixture checks standard/extended readiness and disabled execution without creating another job. Repeat that
check across a restart of only the dedicated daemon, and verify the browser's existing receipt separately.

Application execution remains disabled. No service installation, existing Docker Desktop/WSL workload change,
live data, model/provider request, paid entitlement or production activation is included. A failed or uncertain
run is inspected and its resources reconciled before any fresh attempt; qualification is never inferred from
offline tests. The [reviewable plan](../history/2026-10-26-script-qualification-plan.md) records costs, commands
and acceptance evidence.
