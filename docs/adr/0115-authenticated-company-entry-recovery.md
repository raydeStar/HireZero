# 0115: Let an authenticated member choose an accessible company

Date: 2026-10-26

Status: Accepted; extends ADRs 0020, 0021 and 0114. Verification is recorded separately.

## Context

Hosted sign-in binds invitations in any company, but the API middleware required membership in the selected or
default company before answering either the session bootstrap or company directory. Someone invited only to a
second company could authenticate successfully and then encounter a dead end. A revoked membership or stale
company link had the same problem. Falling back to the default company is insufficient when it is inaccessible too.

## Decision

- Keep `GET /api/companies` authenticated, with the existing stale-member marker check, but independent of
  membership in the selected/default company. Return only active memberships with company ID, name and role.
  All company-specific reads and writes, including company creation, retain their existing authorization.
- When an authenticated `GET /api/session` cannot access the selected/default company because it is absent or
  membership is inactive, return a `companySelectionRequired` session with the verified member identity and the
  same filtered company choices. Do not include workspace, model or configuration data in that response.
  Both session and directory responses use `Cache-Control: no-store`.
- Validate the selection response before rendering, including IDs, names, roles and duplicate IDs. Do not mount
  the workspace or bind its draft storage while a choice is required. Changing from a mounted role to selection
  still reloads the page, clearing in-memory caches under ADR 0114.
- Show an explicit choice even when there is only one accessible company. Preserve that preference and navigate
  to a fresh `/?company=<id>` route; discard task and other workspace query parameters from the inaccessible company.
  Choosing a company does not grant membership: the next request checks it again.
- With no active memberships, explain the situation and offer refresh and hosted sign-out. Refresh can recover
  restored membership. Sign-out keeps the verified cookie-expiry behavior from ADR 0114.

## Limits

This is an entry and recovery path for invited members, not public signup or automatic membership. It does not
cancel previously authorized work, erase recovery receipts, or qualify a production identity provider. Fictional
OIDC middleware tests and synthetic browser-session transport must remain identified separately from real hosted
customer acceptance.
