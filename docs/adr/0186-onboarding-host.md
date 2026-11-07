# 0186 — Onboarding in chat with a HireZero host

Status: accepted, 2026-11-07. Owner request. Builds on [0183](0183-one-sign-off-per-reply.md) and
[0185](0185-the-employee-speaks-and-knows-who-you-are.md).

## Decision

- A new company with no team starts in chat, not in a form. The one answering is **HireZero**, a small built-in role
  (`src/HireZero.Roles.Host`, UI slot `concierge`): conversation only, no deliverables, no soul. It is hired the first time
  the owner answers, kept out of every hiring list, and retired once the first teammate starts.
- Its replies come from the model, following a fixed script in its instructions: the business name, then the website or
  the owner's own description, as much or as little as they like, then the goal; after the brief is approved, what they
  need most, then a recommendation. Private notes on its conversation tell it where setup stands.
- **Each Send is the sign-off for one reply**, exactly as in any conversation (0183), within the plan's limits. No new
  standing approval.
- Research, the brief and hiring are buttons beside the chat, each the owner's own decision, built on the existing
  business records: **Look us up** confirms the business and reads its site (retrying the `www.` address once when the
  site redirects there), then drafts the company brief in one model request. With no website, the brief starts from the
  owner's words. Each line can be rewritten ("This is wrong"), the draft redone ("Try again"), or approved ("Looks right").
  Approving sends "Looks right, that's us." so HireZero's next question is a real reply in place.
- **Yes, bring them in** hires the teammate the approved brief recommends (Marketing today) through the existing goal
  start, with what the owner said they need added to the first assignment. The offer shows the provider, the model and
  the most the first draft can cost before it is pressed (the classic start's run details; for a customer of a hosted
  install, that it is included in their plan, [0187](0187-customers-see-tokens-not-spend.md)), and Yes uses exactly
  those terms; if they can't be loaded, the button stays off. HireZero then retires and Home becomes the new teammate's chat.
- The form stays one click away ("Use the form instead"), remembered in this browser.

## Consequences

- The host is an employee record, so it appears in the team list while it is setting up and as retired afterwards.
- Without a web-search key HireZero cannot find a site by name; it says so and asks for the address or a description.
- Onboarding replies are ordinary conversation tasks with receipts and costs, visible in the cockpit.
