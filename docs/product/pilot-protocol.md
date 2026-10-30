# Context-first pilot: observe useful work

Use this protocol for the approximately ten independent professionals or small agencies proposed in the
[product vision](context-first-vision.md). It prepares observations across all three phases; it does not claim
customers, payments, runtime qualification or product acceptance already exist.

## Before the first participant

Choose one repeatable marketing job with the owner, such as a weekly content plan or a service-page review.
Agree what a usable result contains, which sources may be read, and what the owner will check before using it.
Keep that job and the observation questions consistent across the initial cohort. Record changes to the protocol
with the date; do not quietly change the success definition after seeing results.

Use a dedicated company for each participant in the same approved installation. Obtain authorization before
inviting anyone, importing their information, sending messages, running paid models or charging money. Define
the approved model/compute limits and a contact for problems. A dry run with fictional data comes first.
Real hosted sign-in and tenant isolation must be accepted on the intended deployment. Browser, script and premium
work additionally require the corresponding qualified runtime and its owner approvals. An unqualified feature
stays out of the live session; its phase remains unvalidated.

Record launch prices, included usage, cancellation/refund arrangements and the observation period before making
a real offer. This kit supplies no commercial defaults. Test-mode checkout is an integration check, never paid
conversion. The tool itself makes no network calls and creates no subscriptions or outreach.

## Sessions and evidence

| Stage | Ask the participant to do | Observe and retain privately |
| --- | --- | --- |
| Goal-first onboarding | On Home, describe the business and desired result, optionally supply its website, read the model/allowance disclosure and use the single-run consent plus **Generate my first draft** | Whether the correct business and goal are understood; fields and interactions required; assistance; visible progress and recovery; elapsed time including interruptions |
| First raw result | Inspect the automatically opened draft before changing it; assess whether it fulfills the stated goal and complete it only when genuinely ready | First task and raw artifact references; useful as delivered or not yet useful; missing work, unsupported claims, intervention count and actual model receipts |
| Optional knowledge review | After assessing the first result, inspect website references or use the Business Brief editor to approve/correct reusable company knowledge | Whether website claims stay separate from owner facts; corrections versus preferences; sources and retrieval dates; additional time spent building durable context |
| Recurring work follow-up | Choose a valued recurring job, approve its finite schedule, and review later occurrences | Whether useful work actually caused a return; whether this was observed in a session or reported by the participant; failed or missed occurrences; time spent correcting outputs |
| Advanced work | Once qualified, review a browser/script or delegated developer task and the higher-cost allowance it needs | What additional work was completed; source/permission boundaries; output usefulness; cleanup and cost evidence; whether it displaced a manual task |
| Commercial follow-up | Review an owner-approved paid offer, manage the subscription, and later inspect any renewal | Declined, test-only, paid or renewed; dated provider receipt review; cancellations and reasons; eligibility for the measured follow-up window |

Start by letting the participant work without coaching. Record each intervention instead of concealing it in
an onboarding-time statistic. Ask neutral questions: “What would you use this for?”, “What must change before you
can use it?”, and “Which task would you want repeated next week?” Intent to return or pay is separate from
observed return or payment. Do not complete a task on the participant's behalf merely to improve the metric.

### First-result acceptance

Run the participant's stated goal without coaching them into the product's favorite example. An email request
must be assessed as an email; a weekly content plan must be assessed as a plan. Judge whether the requested work
is actually delivered, its factual claims are grounded and the next action is usable. A brief, a plan to create
the asset later, or a tidy format alone does not establish useful completion. Natural wording and modest length
differences are acceptable unless the participant's task has a real hard constraint.

Retain the first model output unchanged, including its task, artifact version/digest and usage receipt. No assistant
or observer may create an editorial copy, repair the output, rerun it or hide a failed attempt to improve this
acceptance result. Ask the usefulness questions before any participant edits. If the participant then chooses to
edit or separately authorize a revision, preserve both the initial assessment and the later result; record
`useful-after-edits` separately from useful as delivered. An owner-edited result cannot retroactively pass the raw
first-output gate. A subsequent run is a new observation, not a replacement for the original.

Record four timestamps in private session notes: Home entry, Generate activation, first raw result visible, and
usefulness assessment. Also record fields filled, primary actions, assistance count and interruption time.
Backend job or discovery durations are useful diagnostics, but cannot stand in for observed time to a usable
result. The existing Progress export does not measure these browser-visible timestamps or human interventions;
do not derive them from server timestamps or infer an absent observation as zero.

Use the same initial facts/goal when comparing product versions, and retain which version produced each output.
If model terms change before dispatch, record the extra review as friction. For an uncertain request, recover its
saved status and preserve the original attempt; do not authorize another run merely because its response is slow
or missing. Do not spend beyond the participant's approved allowance.

The optional website is reference material for that goal; discovery does not silently turn website claims into
approved team knowledge. A participant without a website can still use Home. Do not route participants through
the longer Business Brief editor as an undocumented workaround for a broken fast path. If that path fails, record
the failure before exploring alternatives.

After each session, retain the observations, source/result references and any consented session notes privately.
Use pseudonyms such as P01 in the manifest. Keep names, contact details, URLs, raw documents, recordings and
provider receipts in the approved private evidence store, outside this public repository. No actual customer
content belongs in fixtures, commits or screenshots checked into source control.

## Capture without a new analytics service

1. Copy [pilot-manifest.example.json](pilot-manifest.example.json) to a private `pilot.json`. The repository's
   `.pilot/` directory is ignored as an optional local working location; an ignored folder is not encryption or
   a backup policy. Use your approved private storage for retained evidence.
2. For each participant who actually starts, add a copy of the object in
   [pilot-participant.example.json](pilot-participant.example.json) to `participants`. Choose `customer` or
   `fictional` explicitly; the template's `unclassified` value is deliberately rejected. Keep dropouts and
   unobserved outcomes in the cohort.
3. As that company's owner, open **Settings → Advanced → Progress → Download metrics**. Save the latest snapshot beside
   `pilot.json` under a pseudonymous filename such as `P01-progress.json`, and set `progressFile` to that basename.
   Use null while no export is available. Retain earlier exports privately for longitudinal comparison, but list
   only one snapshot per company in a given report. Distinct installations with reused local company IDs require
   separate reports; do not rename exported IDs to bypass duplicate detection.
4. Fill the observation choices below. Preserve unknowns as null or `unobserved`. Initial and follow-up dates use
   ISO 8601 timestamps with a timezone. Useful first-deliverable feedback must name the exported first assignment
   and bind the assessed draft with `firstArtifactId` and `firstArtifactDigest` from that assignment's `results`.
   These owner-exported references include provenance and creation time, without the draft text. Set `observedAt`
   to the assessment time, after the result was available. Usefulness does not require approving the result or
   completing its task. Keep the raw-result assessment and intervention/timing notes alongside the manifest;
   the reporter checks the reference, not the truth of the judgment. Never complete a task merely to count feedback.
   Old exports without result references retain their completed-task compatibility path, but do not enter the
   exact-result or first-saved-model-draft counts. An unrun task with no result cannot be useful-draft evidence.
5. Record each completed task's costs in its **Task costs** panel. Use zero only for known zero expenses, retain
   receipt/allocation evidence, then download a fresh Progress snapshot. Company-key model funding and estimates
   are separately labeled. Allocate shared costs deliberately; this report cannot infer business profitability.

| Observation | Accepted values and interpretation |
| --- | --- |
| `correctBusiness` | true, false or null; the observer's assessment of identity matching |
| `assistance` | `none`, `some`, `substantial`, `unobserved`; count any coaching needed |
| `firstDeliverable` | `useful`, `useful-after-edits`, `not-useful`, `unobserved`; set `firstTaskId` only when assessed |
| `firstArtifactId`, `firstArtifactDigest` | Exact assessed result from the owner export; both null when unobserved or no draft was produced. `useful` with an exact reference requires the first saved model output; a later or owner-authored copy must be `useful-after-edits` |
| `repeatUsefulWork` | true, false or null; a dated follow-up assessment of useful return, not a scheduled-task count |
| `repeatEvidence` | `observed-session`, `customer-report`, or null for unobserved repeat use |
| `payment` | `unobserved`, `not-offered`, `declined`, `test-only`, `paid`, `renewed` |
| `paymentReceiptReviewed` | true only after the owner reviews an actual provider receipt for a recorded paid/renewed outcome; false otherwise |

Run the report with the existing Node installation, writing to a new private output file:

```powershell
node tools/pilot-report.mjs .pilot/pilot.json > .pilot/report-01.json
```

The command reads the manifest and named JSON companions only. Companion files must resolve beside the manifest;
path traversal and out-of-directory links are refused. Inputs are bounded to 256 KiB for the manifest and 512 KiB
per export, with at most 100 participants. It does not modify input files or contact any service. Output contains
cohort counts, aggregate metrics and an input digest, without company IDs, task IDs, expense notes or source text.
Use a generic cohort label, not a customer name. The digest identifies these inputs; it is not a provider signature.

## Read the report honestly

Customer and fictional evidence have separate sections and no combined success rate. Each assessed outcome shows
its numerator, assessed denominator, unobserved count, rate among assessed participants and rate across the whole
cohort. Zero assessments yield no rate, not 0% success. `exactResultAssessments` counts judgments bound to saved
drafts. `usefulFirstSavedModelDrafts` counts those marked useful for the first saved model output. It does not prove
that the first request succeeded: retain the initial attempt and failure receipts separately. Owner edits and
later outputs cannot enter that count. Field edits can be preferences as well as corrections.
Multiple request days and scheduled tasks show activity, not useful retention. Historical gaps remain counted.
The report includes initial/follow-up/export date ranges and median follow-up days so different observation
windows stay visible. These summaries still do not establish renewal eligibility or a retention denominator.

The cost subtotal includes only reconciled completed tasks in available exports. The pooled average divides
summed costs by completed-task count, and appears only when every participant has an owner export and every
completed task has a current total. Do not average participant averages or treat a missing export as zero.
Amounts are aggregated to twelve significant digits; source exports retain their original amounts.

Payment figures are observer-recorded counts, including separate test-only and not-offered counts. The reporter
does not authenticate receipts or contact Stripe. “Renewed” is not a retention rate: compare like follow-up windows
and retain the actual eligible-customer denominator and provider evidence in the private commercial review.
Real phase acceptance still requires inspecting those sources and observed usefulness, not this generated JSON.

At the agreed review date, inspect all participants, including unsuccessful and incomplete sessions. Compare
interventions, useful deliverables, actual useful returns, current cost coverage and payment evidence. Fix the
repeated failures before expanding the workload or acquisition effort. The full three-phase goal remains open
until its software, runtime, hosted-service and customer-validation requirements are separately proven.
