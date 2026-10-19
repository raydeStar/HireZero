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

## Ads

Marketing can review ad performance ([ADR 0031](adr/0031-ads-read-contracts.md)).

1. Run **Read ad performance** with 7, 28 or 90 days. It reads, read-only:
   - spend, impressions, clicks, conversions and conversion value, by campaign and by day, for the period and the
     one before;
   - derived from those: CTR (clicks per impression), CPC (cost per click), CPA (cost per conversion) and ROAS
     (conversion value per unit of spend).
2. Unusual days in spend and conversions are flagged. Campaigns with at least 5% of spend are listed when they:
   - spent without converting;
   - had their cost per conversion rise by 30% or more;
   - returned less than they cost.
3. Approve the result, then run **Ads review**. It writes the review and at most three proposals. It cannot change
   anything. Changing a campaign, budget or bid will need its own approval, and is not possible yet.

Google Ads is the first real platform ([ADR 0032](adr/0032-google-ads-provider.md)). Until it is connected, try ads
with `HIREZERO_SYNTHETIC_ADS=1`, which serves fictional campaigns in XTS, the currency code reserved for testing.

### Connect Google Ads

Google Ads needs three things besides your account: a developer token, an OAuth client, and a refresh token from a
user who can only read.

1. **Developer token.** In the Google Ads manager account you use for API access, open *Admin › API Center* and
   request a token. A new token works only with test accounts. Apply for *Basic access* to read real accounts.
2. **OAuth client.** In a Google Cloud project:
   - turn on the *Google Ads API*;
   - create an OAuth client under *APIs & Services › Credentials*. The *Desktop app* type is simplest.

   Note its client id and client secret.
3. **A read-only user.** In the Google Ads account, invite a Google user with *Read only* access. Google's Ads scope
   cannot be limited to reading, so this user is what keeps HireZero from ever being able to spend.
4. **Refresh token.** Sign in as that read-only user through your OAuth client, asking for the scope
   `https://www.googleapis.com/auth/adwords`, and keep the refresh token. Google's client libraries include a small
   example for this. The OAuth 2.0 Playground also works with your own client.
5. **Tell HireZero where to read.** Create `google-ads.json` in the data root:

   ```json
   {
     "customerId": "123-456-7890",
     "loginCustomerId": "111-222-3333",
     "clientId": "your-client-id.apps.googleusercontent.com"
   }
   ```

   - `customerId` is the ads account to read.
   - `loginCustomerId` is only needed when access comes through a manager account.
   - `apiVersion` (for example `"v23"`) may be added when Google retires the default version.
6. **Secrets.** Supply `HIREZERO_GOOGLE_ADS_DEVELOPER_TOKEN`, `HIREZERO_GOOGLE_ADS_CLIENT_SECRET` and
   `HIREZERO_GOOGLE_ADS_REFRESH_TOKEN`, preferably as mounted files ([SECRETS.md](SECRETS.md)). Restart HireZero.
7. Check *Integrations*: **Google Ads** should be active and name the account and API version.

HireZero only sends read queries it composes itself (`SELECT … FROM campaign` and `FROM customer`) to the Google Ads
search endpoint, and signs in only at `oauth2.googleapis.com`.

| You see | Meaning |
| --- | --- |
| "Set the secrets …" | One or more of the three secrets is missing |
| "Google Ads could not be read (HTTP 403, PERMISSION_DENIED)" | The user lacks access to the account, the developer token lacks production access, or `loginCustomerId` is needed |
| "… could not sign in (invalid_grant)" | The refresh token was revoked or expired; authorize again |
| "Google Ads could not be read (HTTP 404 …)" or `UNIMPLEMENTED` | The API version was retired; set a newer `apiVersion` |

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
