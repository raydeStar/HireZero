# A site's own counts fill the weekly numbers

The owner asked to connect analytics so Marketing's morning rounds would have real weekly numbers. Real use found
that the owner's site keeps no Google Analytics: it counts visitors itself, cookie-free, and offers the counts in its
admin and over MCP to a holder of one of its agent keys. The Google sign-in also could not list GA4 properties (HTTP
403). Connecting Google Analytics would have given the rounds nothing.

## What changed

[ADR 0173](../adr/0173-site-analytics.md):
- **A neutral daily read.** `analytics.daily` returns one row per UTC day with visitors, page views, sign-ups and new
  accounts. A count the source does not keep is null.
- **Site analytics.** `HireZero.Integrations.SiteAnalytics` is set up in Company settings → Integrations with the
  site's MCP address and an agent key. It reads with two `tools/call` requests to that address only, and keeps only
  the counts.
- **The weekly numbers form.** Marketing's form reads the chosen week through a read-only endpoint:
  - a finished week fills the visit step, if it is blank, with the week's visitors;
  - all four counts are shown beside the form;
  - a week that is not over shows its counts so far and fills nothing.

  Nothing is saved until the owner saves, so the round still sees only the owner's readings.

The site's counter, a separate project, was changed alongside. It no longer counts the team's own signed-in browsers,
pages left within five seconds without input, or pre-loaded pages. It also now counts the Get started page. Those
changes are deployed with that project, not with HireZero.

## Checks

- Three focused xUnit facts for the provider, against a fake site:
  - it is off until an address and a key are set, and accepts only a safe address;
  - a week is read as one row per day from two calls carrying the key, to that endpoint only;
  - days older than the site keeps are null;
  - a range ending after today is refused;
  - a refused key is reported plainly.

  The fake rejects a request without a Content-Length.
- The secret-list fact now includes the new key, and the integration, isolation, analytics and ladder classes pass
  (22 facts).
- The ladder unit test covers week totals, the visit step and day arithmetic.
- **End to end.** A local copy of the site ran with a throwaway owner and agent key, and the test company on port 5082
  was connected to it through Company settings. The card turned active without a restart. The form filled last week's
  visits and showed "so far this week" for the current week without filling.

## Found by the end-to-end run

The first live read failed with "Your site could not run analytics_summary". HireZero sent the request as streamed
JSON with no Content-Length, and the site's server reads exactly Content-Length bytes, so it saw an empty request. The
provider now sends a sized body, and the fake site rejects an unsized one: the fact failed on the old code and passes
on the new.

## Not done

- The installs on 5065 and 5066 are not updated. The new integration adds an assembly, so the next republish stages a
  new file.
- Only visitors are mapped to a step. Sign-ups and accounts are shown for the owner to place.
- Filling readings with no owner step would override ADR 0029 and is not proposed.
