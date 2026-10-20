# 0037: Loaded action choices

Status: Accepted.

## Context

The owner walkthrough (docs/history/2026-10-19-owner-walkthrough.md, finding 8) found that proposing an ad change
needed a campaign id that the owner could only learn by running "Read ad performance" first. Fixed choices
(`ActionField.Choices`) cover values known at build time, such as 7, 28 or 90 days. A campaign list belongs to the
connected account and changes over time.

## Decision

- **A field can load its choices.** `ActionField.LoadsChoices` marks a field whose current values the module lists.
  `IEmployeeModule.ActionChoicesAsync(action, field, tools, token)` returns `ActionChoice(Value, Label)` pairs. The
  default implementation refuses with a 404.
- **Read only, through the role's tools.** The Host route `GET /api/company/actions/choices` gives the module a
  read-only toolbox scoped to its manifest, as an action gets, so every call is audited and nothing can write. A
  source that cannot be read gives an empty list, and the form falls back to typing the value.
- **Bounded.** The route keeps at most 100 choices. Each value must fit the field's length, and each label is
  bounded to 120 characters. Labels from third parties are clipped by the module.
- **A help, not a rule.** The store does not enforce loaded choices, because the list can change between opening
  the form and running the action. The action still checks the value it receives against the live source: the ad
  change reads the campaign live.
- **First use:** the ad change's campaign field lists the campaigns with activity in the last 90 complete days, as
  "name (status)", leaving out removed campaigns.

## Consequences

- The web shows a field with loaded choices as a dropdown. It loads the choices once each time that action is shown,
  not on every dashboard refresh.
- Building this exposed a fixture inconsistency. The synthetic ads account reported campaigns as `cmp-101` in
  `ads.performance` but accepted only `101` in `ads.campaign` and `ads.change`. It now reports the same id everywhere,
  as the contract says. A test runs the ad change on a loaded choice so the two cannot drift apart again.
