# 0059: Durable inbox and bounded owner email notices

Status: Accepted. Extends durable company events and recurring work.

## Decision

Selected work outcomes create company-scoped inbox alerts in the same transaction as their source event:
employee results, artifacts awaiting review, blocked/uncertain work, recurring outcomes and source comparisons.
Routine progress does not become an alert. A scheduled task creates one recurring-result alert instead of a
second conversation alert. The inbox has stable sequence pagination, a personal unread count and per-member
read/unread state. Reading never approves, retries or changes the underlying work. Existing authenticated
company membership remains required, including for an email deep link.

Migration 17 adds alerts, personal read marks, preferences, verified sign-in email evidence and a delivery ledger.
The inbox is the durable record. It works when no external delivery service is configured.

## Email authority and privacy

Email is optional and off by default. An active owner may authorize generic notices to their own verified
sign-in email. A configured local login string is not email verification. OIDC sign-in records verified email
evidence separately; an unverified or changed email claim removes its current eligibility. Existing members
without that evidence sign in again before enabling delivery.

Consent pins the member, address, membership version, mail-service identity, public workspace URL, selected
categories and a daily allowance of one to 20 emails (UTC). It lasts 30 days. Preference updates require the
exact current version and service digest. Changing consent affects future events; it does not send historical
alerts. Disabling email, membership revocation, expiry, changed service or address prevents further dispatch.

Messages contain only a generic notice and an authenticated workspace inbox link. They omit employee output,
company names, prompts, facts and document excerpts. The selected company is encoded in the link and preserved
through browser navigation; server-side membership checks still control access. The address cannot be edited in
the notification form or replaced by an agent.

## Delivery

The host composes a role-independent SMTP integration; no employee gains an email-sending tool. Operators
configure a hosted HTTPS workspace, SMTP server/STARTTLS port, sender and username, and supply the password
through the existing secret mechanism. The adapter requires STARTTLS and normal certificate validation.
Credentials are never stored in notification records. No new third-party package or runtime pin is required.

The worker durably claims one envelope before attempting it, rechecks consent before dispatch, and gives the
attempt a 20-second local deadline. Already-read or over-24-hour-old pending alerts are skipped. Reaching the
daily allowance keeps the alert in the inbox; it does not create a next-day burst. Server acceptance is recorded
as acceptance, not as proof of delivery to the recipient's inbox.

Any unconfirmed attempt is marked unknown, including one interrupted by restart. It is never automatically
resent. Other emails to that recipient are held until the owner explicitly keeps the uncertain alert in the
inbox without resending it. An acknowledgment releases only new, still-current envelopes. A notification
cannot be recalled once an external send is in flight.

## Experience and limits

A persistent inbox button shows the member's unread count. Alerts link to their employee result, exact schedule
or source review. Email preferences show the recipient, authorization expiry, daily limit and recent outcomes.
Lost command responses use the existing company-scoped operation receipt and survive reload.

This change does not provide SMS, push notifications while the browser is closed, delivery/read tracking,
unlimited email, arbitrary recipients or an always-on hosting guarantee. Mail acceptance and deliverability
require separate operational validation against an explicitly configured service. Retention/deletion remains
part of the broader product goal; alerts and delivery receipts are retained until that policy is implemented.
