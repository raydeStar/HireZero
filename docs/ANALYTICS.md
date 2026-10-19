# Marketing analytics

Marketing can read your website's traffic and search performance, compare it with the period before, and flag
unusual days. You review that evidence, and then Marketing can write a KPI digest from it. Nothing is changed in
Google, and checks never call Google. The decision record is [ADR 0029](adr/0029-marketing-analytics-reads.md).

## How it works

1. On a Marketing employee, run the action **Read web analytics** and choose 7, 28 or 90 days.
2. HireZero reads, read-only:
   - traffic (sessions, users, page views, key events, engagement), by day, channel and landing page;
   - search performance (clicks, impressions, click-through rate, position), by day, query and page.

   Each is read for the chosen period and the one before. Search data ends three days earlier, because Google
   publishes it late.
3. Unusual days are flagged against the same kind of day in the previous period (weekdays against weekdays, weekends
   against weekends), so an ordinary quiet weekend is not flagged.
4. The result arrives as an artifact. Approve it, then run the **KPI digest** skill with it attached as evidence. The
   digest quotes the evidence and labels any explanation as a hypothesis.

Marketing depends on neutral `analytics.*` tools, not on Google. Another analytics source could be added later as its
own integration, without changing Marketing.

## A/B tests

Marketing can size a test before you build it, and read the result when it has run. All the arithmetic is done by
code, never by the model ([ADR 0030](adr/0030-ab-test-design-and-significance.md)).

1. **Size an A/B test** (action). Give it:
   - your current conversion rate, for example `3.2`;
   - the smallest relative lift worth detecting, for example `10` for +10%;
   - the visitors per day entering the test;
   - the number of variants including control;
   - the significance level (usually `5`) and the power (usually `80`).

   It returns the visitors each variant needs and how long to run, in whole weeks. With three or more variants, the
   significance level is split between the comparisons with the control.
2. **Read an A/B test result** (action). Give the control's and the variant's visitors and conversions. It returns:
   - the lift, with its confidence interval;
   - the p-value, and whether the result is significant;
   - a sample-ratio check. If the split between control and variant is far from even, something is wrong with the
     test (a redirect, a bot filter, a broken variant), and the warning comes first.
3. Approve the result, then run the **A/B test plan or readout** skill with it as evidence. It writes the plan or the
   readout using only the computed numbers.

Run every test for its full planned length. Stopping as soon as a result "looks significant" makes false wins much
more likely.

## Try it with fictional data

Set `HIREZERO_SYNTHETIC_ANALYTICS=1` and restart. Marketing then reads deterministic fictional data, labelled
"Synthetic fictional data" in every report, including a planted unusual day every 37th day of the year.

## Connect Google Analytics 4 and Search Console

You need admin access to the GA4 property and owner access to the Search Console property.

1. **Create a service account.** In the Google Cloud console, pick or create a project, then open *IAM & Admin ›
   Service Accounts* and create one, for example `hirezero-reader`. It needs no project roles.
2. **Turn on the two APIs** for that project: *Google Analytics Data API* and *Google Search Console API*.
3. **Create a key.** On the service account, open *Keys › Add key › Create new key › JSON*. A file downloads. This file
   is a secret: put it straight into your vault or a secret file, never into the repository or chat.
4. **Give it read access.**
   - In GA4, open *Admin › Property access management*, and add the service account's email with the *Viewer* role.
   - In Search Console, open *Settings › Users and permissions*, and add the same email with *Restricted*
     permission.
5. **Tell HireZero where to read.** Create `google.json` in the data root:

   ```json
   {
     "ga4Property": "123456789",
     "searchConsoleSite": "sc-domain:example.com"
   }
   ```

   - The GA4 property id is under *Admin › Property details*.
   - The Search Console site is either `sc-domain:yourdomain` for a domain property or the exact URL prefix, such
     as `https://www.example.com/`.
   - Either entry can be left out, and only that part is read.
6. **Supply the key** as the secret `HIREZERO_GOOGLE_SERVICE_ACCOUNT`, for example with
   `HIREZERO_GOOGLE_SERVICE_ACCOUNT_FILE=/run/secrets/google-key.json` ([SECRETS.md](SECRETS.md)). Restart HireZero.
7. Check *Integrations* in the workspace: **Google Analytics and Search Console** should be active, and name the
   property and the site.

HireZero signs in with the key only to `oauth2.googleapis.com`, asking for the read-only scopes `analytics.readonly` and
`webmasters.readonly`. It reads only from `analyticsdata.googleapis.com` and `searchconsole.googleapis.com`. To rotate
the key, add a new key to the service account, update the secret file, and delete the old key.

## When something is wrong

| You see | Meaning |
| --- | --- |
| "Set HIREZERO_GOOGLE_SERVICE_ACCOUNT …" | No key is configured |
| "Name a GA4 property … or a Search Console site … in google.json" | `google.json` is missing or has neither entry |
| "Google could not be read (HTTP 403, PERMISSION_DENIED)" | The service account was not added in GA4 or Search Console, or an API is off |
| "Search: not read (Name a Search Console site …)" | Only the GA4 part is configured; traffic is still read |
| "The Google key names a token endpoint other than Google's" | The key file is not a genuine Google key; it is refused |
