# Plow retirement — 2026-10-31

The owner requested disconnecting the competition-era provider while deferring replacement
signup until the local UI is ready. [ADR 0174](../adr/0174-retire-the-competition-workspace-provider.md)
records that architectural boundary.

The current local app has no Plow runtime dependency. The change is in the separate public-site
checkout: no production client/relay construction; HTTP 410 on retired phone, activation,
provisioning and invitation endpoints; former workspace launches lead to a static access notice.
Old configuration cannot reactivate the integration. Existing records remain readable, with
queue notification actions disabled. The local app requires no rebuild or reinstall.

The website keeps its admin, aggregate analytics, feedback, launch-update list and earlier
Google/Microsoft account management. Current entry pages and rendering no longer promise phone
signup, free hosted workspaces or the competition onboarding guide. Saved CMS revisions remain
unchanged. No model calls, SMS, workspace creation, customer outreach or record deletion were used.

## Verification

- Final Python regression: **92/92 passed**. The backup test now verifies the preserved uncertain
  provisioning record directly instead of expecting a live provider after restore.
- Four retirement tests passed, covering every former action with multiple HTTP methods, old
  configuration, workspace-host isolation, retained queue records, admin analytics and public entry.
- Five JavaScript tests passed, including a cached signup shell that drops private URL fragments
  and navigates to the notice without making provider requests.
- Browser preview verified the access notice and navigation to launch updates. No form submitted.

## Live result

- Website source committed and pushed to `main`: `2082e5a`.
- Source-only bundle: 534,467 bytes, 79 entries; SHA-256
  `1b3fd711b3e9c3197e4974abcc1573894c28c35f5dd7721d257d1bb8d4230ddb`.
- Installed once with the existing backup routine at 20:06 UTC. The earlier interrupted transfer
  was canceled at the incomplete here-document before any install ran; no uncertain install was replayed.
  Pre-install database and normal backup archive were retained. All three site services are active.
- **51 public deployment checks passed**: retired APIs return 410, asset hashes match, the access
  notice and revised guide are live, and private/admin/MCP routes still require authentication.
  The former workspace wildcard host also returns 410 through nginx.
- Before/after records: zero earlier customer accounts, zero queue entries, six historical workspace
  setup records and three CMS documents. Those six setup records are not evidence of six customers.
- The local app still reads the site's authenticated aggregate analytics: the October 19 week has
  12 daily visitors summed across the week, 13 page views and zero launch signups/accounts.
  No readings were saved. The morning shift remains active with a current review, zero occurrences
  consumed and its next run at November 2, 08:00 Mountain.
- Browser proof and compact logs remain in the ignored pilot folder. Unrelated public-site working
  tree changes were left untouched. Local app binaries and company data were not changed.

The integration is disconnected. Existing provider-hosted resources were not deleted or canceled;
decommissioning those resources requires identifying what to retain first. Replacement signup stays deferred.
