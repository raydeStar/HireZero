# Owner walkthrough, 2026-10-19

This is the first walk through HireZero as an owner would use it, done in a browser to judge it as a product rather
than as code.

**Setup**
- The compiled browser fixture (`--measured-browser-fixture`) with a fake model, so replies worked without any spend.
- Every synthetic integration switched on: analytics, ads, email and CRM.
- Browser pane at about 1024×768, plus a phone-width check at 375×812.

**What I did**
1. Hired a Support employee through the guide.
2. Wrote and approved its department context.
3. Ran its intake action.
4. Asked it a question and authorized one reply.
5. Ran Marketing's ad-change flow from reading to publication.
6. Visited Departments, Today, Budgets, Members, Audit, Integrations, Plans and Board.

## What already works well

- **Guided hiring:**
  - role defaults fill sensibly;
  - a new department can be created inline;
  - the review step shows exactly what will be shared before approval.
- **The approval chain for an external change.** A Google Ads–style pause went reading → approve → propose → approve
  → "Publish once" → confirmed result, and every step was clearly worded. This is the core safety story, and it reads
  well.
- **Redaction.** The intake action's output redacted the email and card number correctly, and flagged billing and
  privacy for a person.
- **Members and Audit** are short and clear: "Audit log: intact, 34 entries", with an export.
- **Phone width** works: no horizontal scroll, and a bottom tab bar.

## Findings

Severity: **P0**, broken or unsafe; **P1**, makes daily use hard; **P2**, polish.

### P0: broken or unsafe

All four are fixed on `fix/walkthrough-p0`. Each is checked by `tests/walkthrough-browser-smoke.mjs` in headless
Chrome at 1024×768. Number 1 is also checked by `SupportModuleTests`.

1. **Action input is stored and shown unredacted.** Running Support's "Prepare customer messages" saved the raw input
   as the task body, and the task card displays it as JSON with the card number and email intact. Only the artifact is
   redacted, so the privacy promise holds for models but not for storage or the screen.
   - *Fix:* let a module normalise action input before it is stored, as `ValidateInput` already does for skills, and
     have Support redact there.
   - **Fixed.** The store now passes action input through the module's `ValidateInput` before saving it. Support
     redacts there, and the intake report counts the redaction markers.
2. **Action fields are single-line, so multi-line input is impossible.** Line breaks are stripped, so messages
   separated by `---` lines arrive as one. Support's intake cannot split messages entered through the UI.
   - *Fix:* use a textarea for long fields, for example a `Multiline` flag on `ActionField`, or any `MaxLength` above
     200.
   - **Fixed.** Fields longer than 200 characters are multi-line: Enter adds a line, and Ctrl+Enter runs the
     action.
3. **The authorization tick box is silently disabled.** In a conversation reply, "I reviewed… I authorize" cannot be
   ticked until "Review reply and context" has been opened, and nothing says so. I first thought the button was
   broken.
   - *Fix:* load the context with the card and enable the box, or label the box "Open the review first".
   - **Fixed.** The rule stays, but the card now says why the box is locked, with a "Review the reply's context to
     unlock" button beside it. The hint disappears once the context is shown.
4. **The conversation is squeezed into a tiny scrolling box.** At 1024×768 the transcript, including the reply being
   authorized, sits in a strip about 120 pixels tall, and the composer area overlaps "Gather evidence". It is hard to
   read what you are approving.
   - *Fix:* give the transcript the page's scroll, and keep only the composer fixed.
   - **Fixed.** The transcript now keeps at least `min(300px, 38vh)`, about 300 pixels at 768 tall. The header is
     capped at 22% of the height and the composer at 30%, and on very short screens the column scrolls rather than
     overlapping.

### P1: daily-use friction

5. **Asking one question takes about six steps and a wall of jargon.** You choose a work type and a message action,
   enter an allowance, prepare, open the review, tick and send. The card speaks of "native turn", "8,000 estimated
   provider tokens across task, employee, department and company budgets", "soft stops", "policy oauth-measured-v1"
   and "2,072 / 4,096 UTF-8 bytes".
   - *Fix:* a one-line summary ("Ask Support partner · up to 5 credits · Send"), with the safety details in a
     collapsed section, and a remembered default allowance.
   - **Partly fixed (feature/plain-language).** The card now opens with one line ("Sends one reply · intended
     allowance 5 credits · expires 7:16 PM"), and every technical paragraph sits in a collapsed "Safety details"
     section. The number of steps and a remembered default allowance are still open.
6. **Raw ids are everywhere:**
   - the work-type menu (`support-reply`, `kpi-digest`, `ads-review`);
   - task cards (`support-intake · awaiting_review`);
   - artifact provenance (`action:ads-change · needs_review`);
   - Today (`Support partner: support-intake`);
   - onboarding field labels (`product`, `customers`, where the helpful question is only a placeholder).
   - *Fix:* show skill, action and field names, and plain states ("Waiting for your review").
   - **Fixed (feature/plain-language).** One shared `labels.ts` shows names instead of ids:
     - skill and action names (served as `skillNames`) in menus, task cards, provenance and Today;
     - plain states ("Waiting for your review", "Needs your review");
     - role names in lists;
     - readable onboarding fields, with their question shown as help text.

     The exact-context review panel keeps its technical ids on purpose.
7. **Raw JSON is shown to the owner.** The task input appears as `{"messages":…}`, and the ad-change reading ends with
   a `Call: {"campaignId":…}` line meant for the machine.
   - *Fix:* hide or format both.
   - **Partly fixed (feature/plain-language).** Structured task input now shows as labelled fields. The ad-change
     reading's `Call:` line remains: the publication parses it, so it needs its own change.
8. **Actions are hard to find and use:**
   - Every action sits inside a collapsed "Gather evidence" section; Marketing has seven in one long list.
   - Choices are free text: "Days (7, 28 or 90)", "Change (pause or budget)".
   - An ad change needs a campaign id that you can only learn by running "Read ad performance" first.
   - *Fix:* a visible actions menu, select fields for fixed choices, and a "Propose a change" button next to each
     campaign in the ads reading.
   - **Fixed (feature/company-hub, feature/ads-propose-change):**
     - the section is now "Actions", with an Action picker that shows one form at a time;
     - fields with fixed values declare `Choices`, are shown as dropdowns, and the store refuses any other value;
     - the ad change's campaign is now a dropdown of the account's campaigns by name and status, loaded through
       read tools when the form opens (feature/ads-propose-change, ADR 0037), so no reading is needed first.
9. **Stale or contradictory copy:**
   - "Approve the reviewed artifact first" shows after it was approved.
   - "Message prepared… Send & authorize" stays after the reply completed.
   - "Approve content" and "Request changes" remain after approval.
   - Finished tasks still offer "Cancel" and "Complete after review".
   - **Fixed (feature/plain-language):**
     - the publication instruction now says what is true;
     - "Message prepared…" clears once the reply leaves the waiting state;
     - "Approve content" and "Request changes" show only while a result needs review;
     - "Cancel" is hidden on work whose results are all approved.
10. **Company-wide screens are buried.** Today, Budgets, Members, Audit and Integrations are collapsed sections at the
    bottom of the sidebar, under the employee list. The "Company" tab only manages departments.
    - *Fix:* make Company a real hub, or give Today its own tab, since it is where "needs you" lives.
    - **Fixed (feature/company-hub).** The Company tab is a hub with sections: Today (first), Departments, Budgets,
      Members, Integrations, Audit log and Companies. The section is kept in the address. The sidebar keeps
      conversations and people, plus one "Today, budgets, members and settings" link to the hub.
11. **Integrations can only be set up by editing JSON files in the data root.** Google, Google Ads, Azure DevOps,
    OpenRouter and agent apps each say "name … in …json in the data root". There is no in-app setup.
12. **Budgets are tokens, with ambiguous rows.** Several rows read just "Plan allowance", with nothing saying which
    task they belong to. Dollars appear nowhere, although API models now have prices.
    - **Fixed (feature/dollar-budgets):**
      - Budgets opens with "Spend": dollars spent to date for the company and for each employee, from each job's
        recorded usage and the reviewed price table. Jobs on a model with no price, such as Codex, are counted and
        named as such, never guessed.
      - Each work allowance says what it is for: "employee: work", "Plan: objective" or "Meeting: title". Allowances
        that reserve nothing fold under "Nothing reserved".
      - Limits stay in planning tokens and say so. Company-level dollar limits would be a separate decision, as ADR
        0036 notes.
13. **The work log is noisy.** One ad change wrote five near-identical lines: proposed, approved, started, ready to
    send, sent, published.
    - **Fixed (feature/quiet-work-log).** The work log shows one line per piece of work: its latest step, with the
      work's name, and the day's earlier steps folded under "N earlier steps". An event about an artifact,
      publication, run or reply counts toward the task that produced it.

### P2: polish

14. Opening `http://localhost:5077` shows a blank page; only `127.0.0.1` works. There should at least be a message.
    - **Fixed (feature/polish).** A direct loopback page request for `localhost` on the workspace's own port is
      redirected to the 127.0.0.1 address. The API is never redirected, and neither is any other name or port, so the
      DNS-rebinding guard stands. Other refused requests now say where the workspace is served.
15. Accessibility:
    - sidebar employee entries are buttons with no accessible name, so a screen reader hears "button";
    - the allowance input has no label.
    - **No longer reproduces (feature/polish).** A DOM sweep of every view found no control without an accessible
      name or label. The plain-language pass had already added the labels.
16. Alignment is uneven: a newly hired employee and the department list items are indented or centred differently
    from their neighbours.
    - **Fixed (feature/polish).** Composer labels inherited a centred column layout, and sidebar buttons inherited
      centred content. Both now line up on the left.
17. Jargon and leftovers:
    - the header badge "Manual jobs connected" on every page;
    - the internal model name "Luna" in copy;
    - a default company literally named "Company".
    - **Fixed (feature/polish).** The header badge shows only when something is unusual ("Synthetic test provider",
      "Replies unavailable"). "Luna" is gone from the copy. An unnamed company is "My company".
18. Board offers "Choose CEO chair" with no CEO hired, and no hint to hire one first.
    - **Fixed (feature/polish).** With no CEO, the chair picker is disabled ("No CEO hired yet") and the form says to
      hire and onboard a CEO first.

## Suggested order

1. **P0 1–4.** They are small and contained, and two of them (unredacted storage, the single-line field) undercut
   features we just built.
2. **Findings 5, 6 and 9 together, as a "plain language" pass.** One shared vocabulary for skills, actions, states
   and fields; a short authorization summary with the details collapsed.
3. **Findings 8 and 10 as an information-architecture pass:** a Company/Today hub and visible actions.
4. **Findings 11 and 12** once real use starts: in-app integration setup and dollar budgets.
