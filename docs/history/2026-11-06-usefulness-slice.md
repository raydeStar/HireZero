# Usefulness slice · November 6, 2026

The owner found Home off-putting: on arrival you still have to work out what to look at, and the right-hand view
of what the team is doing felt hidden. This slice, for the Agent Rush recording, changes presentation only. No new
model request, command, setting or stored data.

## What changed

- **The employee speaks first.** Home's "Today so far" line and count buttons are replaced by a note in the
  employee's voice: what they just finished (with **Read it**), what they are working on (with **Watch**), what needs
  your go-ahead (with **Show me**), what comes next, or that they are free. The most recently active employee leads.
- **One box for a new outcome.** "What do you want to happen next?" with two examples. It fills the goal form below
  and focuses it; the model, cost and consent stay in that form, so nothing runs from the box. It replaces the
  **New task** button.
- **The cockpit stays docked from 1024 px** (was 1280 px), in a 272 px column below 1280 px. It gains **New
  outcome**, which opens Home's box, and **Add something** when the queue is empty. Rows fade in and running work
  pulses. Reduced-motion preferences turn the animation off.

## Checks

- Web type check, format and build passed; `company-ui`, `company-creation-ui` and `goal-flow` Node tests passed.
- `goal-start-browser-smoke` (updated for the note and the outcome box) and `goal-ladder-browser-smoke` passed.
- `company-browser-smoke` fails at "ready to click Departments" on `main` as well; it predates this change.
- Inspected at 1366, 1100 and 375 px in the measured-browser fixture: no horizontal overflow.

## Not done

Daily proactive planning, a quick numbers check-in on the note, and steering the queue (reorder, skip) remain the
next usefulness steps. Shifts and the goal ladder already exist; they were not exercised on the hosted install.
