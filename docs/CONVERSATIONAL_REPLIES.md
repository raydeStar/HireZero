# One reviewed conversational reply

The company UI can request one informational reply through the existing dedicated native OAuth connector. This is a functional backend/stdio path, tested with a compiled fake runtime. No live conversational inference was performed for this change. Default startup without the pinned Codex profile still cannot call a model.

## Owner flow

1. Open an active employee's private conversation, or select **Reply as** in the company conversation. Conversational work uses the selected Luna text route, including Developer advice; the coding route is excluded.
2. Leave **Message action** at **Prepare one reply for review**, enter the message and an explicit intended existing-credit allowance, then use **Prepare reply for review**. This atomically saves the message, an internal accounting turn and its immutable packet. It sends no native request.
3. Choose **Review reply and context**. The sealed review includes recipient/role/thread/scope identity, responsibility, all application context, selected evidence and employee instructions. Optional oldest history may be packed to fit; required facts, notes and evidence are never silently dropped. Later messages are excluded from an already prepared packet.
4. Review the soft-limit risk, acknowledge the exact packet, then choose **Send & authorize one reply**. This approves one native turn only. It does not approve a CEO plan, hire anyone, run coding tools, create autonomous tasks, publish or authorize follow-up turns.
5. A dispatched turn shows **Reply pending** and **Stop reply**. Text is buffered until completion and usage checks pass. The reply and measured receipt survive reload. Canceled, failed, anomalous or unknown outcomes are withheld and have no replay/resume control.

**Save message only** stores a note without a model call or an autonomous job. Company save-only text is an owner note. An explicitly selected non-conversational work type retains its existing separate task/approval workflow.

## Separate contexts

Company replies use a dedicated company-scoped thread for the explicitly selected employee. They include approved company facts and that company thread's bounded history. Employee responsibility, department facts, private notes, private/sibling transcripts, handoffs, roster/private-thread metadata and other work metadata are excluded. No employee is silently selected as CEO or granted authority.

Private replies use the exact employee/private thread, its approved facts and notes, bounded history and selected approved evidence. Company and sibling transcripts are excluded. Scope and recipient are part of the sealed application packet. Company thread reads return no private notes. Existing task, handoff, plan and workspace paths cannot turn a company conversation into repository work.

The internal turn is server-owned `Kind=conversation`. Ordinary workers and strict dispatch cannot claim it. A successful response is stored in the transcript and completes only that turn; it creates no approved artifact, child task, plan or execution grant. Generic retry/reassignment and artifact promotion are refused. A fresh reply needs fresh preparation and authorization.

## Native controls and limits

The production connector is unchanged: the dedicated profile, pinned executable, exact text route, ephemeral thread, zero ambient instruction sources, verified process-local MXC restrictions, scratch-only reads, no network/tools/retrieval, shared provider gate and no automatic replay remain required. No credentials, login, security settings or access were changed. Strict/automatic qualification remains false.

The application packet has a hard 4,096-byte limit and the grant admits one native turn. Native internal retries may still occur. The 8,000-token reservation is a planning estimate, not a provider-enforced cap. The 60-second deadline, 2,000 visible characters, 1,000 output-token target and 8,000 observed-token tripwire are soft stops. The owner-entered credit allowance records intent and cannot guarantee a credit cutoff. Included usage must be available; there is no purchase, recharge, API-key or provider fallback.

Full measured input/output/reasoning usage is retained, including on rejected content. Known complete usage settles once; overage, inconsistent/partial usage or unconfirmed cleanup retains the required hold. Cancellation never promises an instantaneous stop to remote generation/billing. Quota/account anomalies stop continuation; another turn cannot bypass the persistent provider hold.

## Proposed first live check — requires fresh approval

Select one existing active employee in **Company conversation**, using company context only. Prepare exactly this fictional message:

> In one sentence, explain who approves company decisions. Do not create tasks, change plans or perform actions.

Review the actual generated packet and choose an explicit intended existing-credit allowance before authorizing. The proposed approval is for one Luna, low-effort, text-only native turn and its measured receipt. It does not authorize another turn, a purchase, new sign-in, tools, repository access, automatic work or publication. Actual token/credit cost is unknown in advance and may exceed local soft targets. Earlier qualification or Marketing approvals do not authorize this check.

## Local verification

`tests/HireZero.Company.Tests --conversation-checks` exercises the real HTTP and compiled fake native-stdio path: atomic/idempotent preparation, exact scope/context, private-note omission, no strict dispatch, full receipts, no artifact/plan promotion, bounded history, required-context refusal, restart, errors/quota, usage anomalies, cancellation and late receipts. The full Company suite includes these checks.

`tests/conversation-reply-browser-smoke.mjs` uses a disposable profile/host and compiled fake only: company/private/Developer replies, readable packet approval, duplicate sends, history reload, pending/Stop, withheld partial native deltas and no replay. Existing manual draft, handoff, hiring and conversation-navigation checks remain regression gates. Use a separate test port/web root to preserve a running local app.
