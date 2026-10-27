# Member setup and work access — 2026-10-26

The member walkthrough found owner-only hiring, employee settings, shared-fact editing and budget forms offered
to viewers and approvers. Viewers also saw conversation, result-review and work-management actions that the
server would refuse. These were misleading affordances: ordinary membership looked like a series of broken
buttons, and hiding a form without keeping its saved records would have made the read-only experience worse.

The workspace now supplies the current membership to its setup and work components. Owners retain setup controls.
Approvers retain conversations, private-note editing, saved knowledge review, result review, work management,
plans and meetings. Viewers can read the corresponding saved records and navigate to work without mutation forms.
Checkout grants remain owner-only. Measured work still exposes its saved context packet to viewers, and handoff
receipts still link to the assigned work. Home and empty-state guidance now reflect the member's available actions.

This follows [ADR 0020](../adr/0020-members-roles-and-sign-in.md) and the existing Core command table; it changes
no backend role policy. In particular, `knowledge.save` is owner-only while `knowledge.review` permits approvers.
The server remains the authority for every command. The legacy local-owner interface retains its existing default
when no membership role is supplied; an unrecognized nonempty role receives no management or work controls.

Visual review found two further issues. Viewer result and cockpit labels still said "your review" despite there
being no review permission. Those labels now describe the saved status without asking the viewer to act. Long
connection titles also widened the cockpit's grid and caused an internal horizontal scrollbar. The grid can now
shrink within its panel, titles truncate, and descriptions wrap.

## Verification

- Fictional test-host build: zero warnings/errors.
- Web typecheck and production build: passed. The existing bundle-size advisory remains.
- Three focused server membership facts: passed, zero failures/skips.
- Thirteen UI test entries: passed, covering measured authorization/context review, execution/publication controls
  and handoffs. Viewer checks retain readable packets/receipts while preventing work changes; approvers do not see
  checkout-grant forms.
- Extended integration/member browser journey: actual disposable viewer and approver memberships, Home, People,
  departments/shared facts, budgets, conversation/private notes, plans, meetings, priorities and saved results.
  A viewer reads the result, and an approver approves it and completes its task through the UI. Direct forbidden
  commands return 403 before input processing. Connection isolation and restored owner setup remain covered.
- Owner business-drafting regression: discovery, exact consent, brief edits, hiring, first-assignment preparation,
  reload/draft recovery, uncertain-response reconciliation without replay, result review and completed progress all
  pass with fictional sources and a compiled fake model.
- Viewer and approver work layouts inspected at 1440 and 390 pixels. No page overflow; the desktop cockpit also
  passes an explicit internal-width check with the long connection title. No uncaught browser exceptions.

Private receipts and screenshots remain under ignored `test-results/`: `member-journey-*`, `member-work-*`,
`integration-isolation-browser.json` and `business-drafting-browser.json`. Browser fixtures use fresh profiles,
disposable company records and loopback HTTP, then remove their owned temporary files after process exit.
There were no live provider/model calls, deployment or changes to WSL services, runtime images or application
execution settings. Hosted authentication and real customer acceptance remain separate.

The prepared script-qualification source supplement was refreshed for the changed fixture/UI-test sources only.
All 549 previously transferred source files were checked for unexpected drift; the prepared supplement now has
16 entries and would produce 556 source files. It has not been transferred or run, and the image/runtime candidate
is unchanged. Script qualification still awaits its separate owner decision.
