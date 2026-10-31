# 0173 - A site's own counts fill the weekly numbers

Status: accepted, 2026-10-31. Builds on [ADR 0029](0029-marketing-analytics-reads.md) and
[ADR 0170](0170-goal-ladder-and-weekly-numbers.md).

## Context

The weekly numbers came only from the owner ([ADR 0170](0170-goal-ladder-and-weekly-numbers.md)), and filling them from
connected analytics was left for later. The only analytics connection was Google's. Some sites keep no Google
Analytics at all and count visitors themselves, cookie-free. Their admin shows daily visitors, page views, sign-ups and
new accounts, and an MCP endpoint offers the same counts to an AI employee holding one of the site's agent keys. An
owner with such a site had to copy each week's numbers by hand.

## Decision

**A neutral daily read.** The analytics tools gain `analytics.daily`: one row per UTC day with visitors, page views,
sign-ups and new accounts. A count the source does not keep, or no longer keeps for that day, is null. The call reads a
date range ending no later than today.

**A site analytics integration.** `HireZero.Integrations.SiteAnalytics` provides `analytics.daily`:
- It is set up in Company settings → Integrations → Site analytics. The owner gives the site's MCP address, which must
  be https, or http on this computer, with no sign-in, query or fragment, and pastes an agent key from the site's
  admin.
- Each read is two MCP `tools/call` requests, `analytics_summary` and `signups_daily`, to that address only, with the
  key. Days are counted back from today. Page views reach back 90 days and sign-ups 400; older days are null.
- It follows no redirects, sends a sized body, and keeps only the counts from the replies. Like every integration, it
  knows nothing about roles.

**Filling the form, not the round.** A read-only endpoint, `/api/company/employees/{employee}/analytics/daily`, invokes
the read through the employee's own read-only toolbox. Marketing declares the tool, and its weekly numbers form calls
the endpoint for the week chosen:
- For a finished week, the step that names a visit, such as "Visit the site", is filled with the week's visitors when
  it is blank.
- All the counts are shown beside the form, such as "12 visitors · 14 page views · 0 sign-ups · 0 new accounts".
- A week that is not over shows its counts so far and fills nothing, so a part week cannot be saved as the whole week.
- Nothing is saved until the owner checks the numbers and saves. Without a connection, the form is as it was.

The numbers still reach the morning round only as the owner's saved readings. This keeps ADR 0029's rule that analytics
evidence is seen by the owner before an employee works from it.

## Consequences

- An owner with a cookie-free site fills a week with one look and one click, instead of copying from another admin.
- The fill covers visitors only. Sign-ups and accounts are shown but not mapped, because a ladder's sign-up step may
  count starts, not completions. Mapping other steps is a later, owner-confirmed choice.
- The round still depends on someone saving each week. Filling readings with no owner step would need a decision that
  overrides ADR 0029.
- The agent key a site issues may allow more than reading, such as saving drafts. HireZero calls only the two read
  tools. A read-only key scope belongs to the site.
- No Google Analytics is involved. A Google-only owner sees no change.
