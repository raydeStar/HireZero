# Paid extended scripts and preserved results

The all-three-phase goal remains active. This increment advances P3.6 and the shared lifecycle/accounting
requirements. It does not complete Phase 3 or qualify a production runtime.

## Implemented

- An explicit extended batch-script tier, bounded to 1,800 seconds and to the lower per-run limit of the
  company's verified paid plan. Standard scripts keep their 120-second maximum. Memory, storage, network,
  input/output and secret boundaries are unchanged.
- Exact plan entitlement captured in each extended review, rechecked during admission and execution.
  Missing payment, changed plans and withdrawn runtime capability block new work or cancel active work.
- Shared monthly compute reservations, retained uncertain holds and settlement after confirmed cleanup.
  Recovery never reruns a script. Previously completed output remains reviewable after payment lapse while
  its task and source authority remain valid.
- Billing plan and checkout reviews show the extended per-job ceiling. Script reviews show the chosen tier,
  full allocation and no-extra-purchase consequence. Pending reviews explain changed entitlement; Usage
  identifies extended work and distinguishes allocated seconds from a monetary bill.
- Separate optional extended runtime qualification. The explicitly enabled operator suite adds a successful
  execution beyond 120 seconds, an extended deadline and recovery/release checks. It was not run here.
- Readiness monitoring now runs independently of the job queue. The audit found that a long job previously
  postponed the next readiness probe until it finished. The separate monitor preserves the existing
  30-second interval and feeds capability withdrawal into active authority checks.

Decision: [ADR 0086](../adr/0086-paid-extended-scripts.md). Operator instructions are in the
[sandbox guide](../../runtime/sandbox/README.md); catalog bounds are in [Settings](../SETTINGS.md).

## Verification

- Initial backend selection: **99 facts passed**, covering premium bounds/entitlements, billing, delegation,
  sandbox authority, qualification and Stripe parsing. Ten new facts cover contract compatibility, paid and
  qualified admission, exact plan changes, company isolation, durable holds, no replay, in-flight payment or
  runtime loss and the extended qualification gate. The worker's two offline tests also passed.
- The follow-up host selection passed **12 facts**, including the new independent-monitor test during a
  fictional long job, eight premium reruns and three existing host startup/recovery/HTTP facts. This brings
  the increment to **103 unique passing backend facts**, with eleven new facts. The monitor test uses the
  actual 30-second readiness interval and confirms cancellation, withheld output, cleanup and settlement
  without a real container.
- **Three distinct Chrome journeys passed**: the new premium flow and the existing standard-script and
  billing regressions. The premium flow covers two companies, explicit fictional plan/price and script review,
  reload on a 390px screen, a lost approval response with only one execution, a 180-second hold, payment loss,
  refusal of a pending run, successful cleanup, retained allocation and completed output review after lapse.
- Desktop/mobile screenshots were inspected. The mobile receipt was improved to load the saved review at
  mobile size and assert its heading is visible, avoiding a misleading screenshot taken midway down a
  resized page. Neither review nor usage has horizontal overflow at 390px.
- Frontend typecheck/build and affected .NET builds passed. C# and frontend formatting and tracked whitespace
  checks passed. The existing frontend bundle-size warning remains.

Compact logs, screenshots and JSON receipts are retained under ignored `test-results/premium-*`. Prices and
providers are fictional. No live model requests, payments, GPU work, container launches, runtime upgrades,
commits, publications or deployments occurred. Owned fixture processes remove their temporary profiles and
data after exit. Free disk space stayed above 315 GiB.

Production extended containment qualification, hosted Stripe acceptance, launch plans/prices, broader premium
environments and actual compute-cost accounting remain open. The delivery ledger records P3.6 as partial.
