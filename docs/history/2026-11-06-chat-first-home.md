# Chat-first Home · November 6, 2026

The owner wanted the conversation to be the main window rather than a place you drill into: collapsible output,
feedback on and approval of a stated plan, what is running in view, and an easy way to start a shift. Presentation only:
no new command, model request, setting or stored data.

## What changed

- **Home is the desk once work has begun.** It opens on the lead employee's conversation (the employee with the most
  recent work; a picker switches when there are several). The thread is that employee's private conversation with
  the most recent hands-on work, so a goal's draft and its review controls are in front of you. Scheduled rounds come
  in through their own card instead of moving the conversation.
- **The view is chosen on arrival.** A goal started from the overview creates its task partway through a multi-step
  start, so Home never swaps the overview for the desk while the owner is on it. `?home=overview` opens the overview
  directly.
- **Header:** the employee, what they are doing, the role's control (Marketing: **Start shift**, or "On shift · 1 of
  5 done · next today at 7:05" with Pause/Resume), **New outcome** and **Overview**. The overview is the former Home
  with **Back to chat**.
- **In the thread, after the last message:** the latest morning round with its jobs, **Do it** and feedback; the goal
  ladder while it is offered, being worked out or waiting for approval; then the employee's note (running work with
  Watch, what needs a go-ahead with Show me, what comes next). The cockpit still lists everything.
- **Folding.** A long reply shows its first lines with **Show all**; the newest reply starts open. **Collapse all** /
  **Expand all** in the conversation toolbar reach every fold and result at once.
- **Start shift** opens the existing shift form in a dialog with quick defaults: the first round about five minutes
  from now, five mornings, every day. The owner still ticks the consent box. Without an approved goal ladder it says so
  and opens the ladder in the overview.
- Copy: "1 private draft" for a one-round shift; "using Vultr" instead of "with your Vultr".

## Layers

The desk is core (`web/src/company/Desk.tsx`, `Fold.tsx`). The shift control is Marketing's, contributed through a new
`homeAction` on the role's company UI and shown with its `home-lead` slot, so no manifest changed. Cards learn where
they are drawn through `placement: "chat"`; forms that start something stay behind the role's button.

## Checks

- Web type check, format and build passed; Node unit tests 55/55. No server code changed.
- Browser checks passed: goal-start, goal-ladder, marketing-shift (with new desk steps: Back to chat, "On shift · 1 of
  3 done", today's round in the thread, Collapse all, Overview), measured, team, company-chat, business-brief,
  delegation, planning-recovery, settings-recovery, company-scope, action-recovery, integration-isolation, schedules.
  Five checks that read Home's cards open the overview (`home-start` preference or `?home=overview`).
- **Open:** conversation-reply passes 3 of 7 on this branch and 4 of 4 on main, failing where the owner ticks the
  authorize box for a developer reply in its private conversation. Removing the reply fold did not change it; with a
  short trace after the tick it always passed. The cause is not found yet.
- Inspected at 1366 and 375 px in the measured fixture.
