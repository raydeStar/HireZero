# Approved artifact to explicitly assigned work

The next company workflow closes the gap between reviewing an artifact and asking another department to use it. The owner can now share one exact approved artifact and create one specifically assigned task in the same transaction. No model, checkout, repository write or execution grant is created by this action.

## Use the local UI

1. Review the source artifact's complete visible content and approve that exact version.
2. Under **Share approved work and assign a task**, choose an active recipient. The list shows employee, department and role.
3. Choose the recipient's exact conversation, work type, and task instructions with acceptance criteria. Developer roles default to their declared execution skill; role metadata controls this choice.
4. Choose **Share this artifact and assign work**. Selecting a person or conversation alone submits nothing. The UI opens the recipient's assigned work with the shared artifact attached.
5. Developer work remains blocked at the separate execution qualification/approval gate. Other assigned handoff work is also held from automatic dispatch. A supported measured draft can later receive its own explicit one-job authorization.
6. **Revoke this handoff** revokes sharing and cancels/fences only the exact unfinished recipient task. Completed historical work remains recorded. New work requires a fresh explicitly reviewed handoff; retry or reassignment cannot silently copy the old sharing authority.

The receipt binds source artifact ID, digest and approval version to the recipient employee, conversation and new task. The sender's private notes and conversation are excluded. Another task in the same recipient conversation receives no automatic copy. Existing explicitly conversation-scoped historical handoffs keep their original scope; this new UI creates task-scoped handoffs.

Changed or withdrawn source approval blocks admission and result acceptance, including reapproval of unchanged text under a newer version. Source provenance and revocation remain checked during execution fencing; excessively deep provenance chains fail closed. A lost response is reconciled through its saved operation ID, and repeating that same operation returns the original task/receipt. No replacement task, retry or model call is started after restart.

## Developer boundary and remaining work

The real owner HTTP entry now connects cross-department artifact review to an assigned Developer task. Fixture validation continues that same task through the existing bridge with a disposable isolated Git checkout, separately reviewed execution grant, compiled fake coding runner, durable text diff and exact owner content review. Those downstream execution methods remain test-only; production has no qualified live coding factory or automatic isolated-checkout provisioning UI.

Before real execution, complete coding-adapter isolation, checkout identity/write confinement, complete usage and termination qualification. Then obtain separate approval for one exact Developer task, repository/revision, isolated checkout, permitted file/write/network scope, pinned runtime and spending/usage contract. The completed direct qualification calls and single Marketing queue test provide no Developer execution authority. An approved handoff is data-sharing/task authority only, and creative/content approval grants no publication or push authority.

## Verification

- Full solution and frontend TypeScript/Vite builds passed; the existing frontend bundle warning remains.
- All six executable suites passed: Company 308, Core 420, Developer 642, Host 87, Integrations 54, Policy 44.
- Six Node/UI test files passed; both launcher suites passed 14 checks each.
- Real Chrome verified approved Marketing artifact to explicit cross-department Developer assignment, no execution authority or extra dispatch, scoped revocation and desktop/mobile rendering. It used the test executable's compiled fake provider and disposable data only.
- HTTP tests covered unapproved/stale source, wrong digest/version/recipient conversation/skill, source-private isolation, sibling-task exclusion, atomic receipt replay/conflict, source reapproval, retry escape prevention, revocation, restart persistence and no automatic strict-provider dispatch.

This code-only follow-through made no new live model call, performed no real repository execution, restored no expired preview, changed no security/account setting and pushed no commit. The earlier fictional Marketing draft and its one usage receipt remain preserved for the owner's review.
