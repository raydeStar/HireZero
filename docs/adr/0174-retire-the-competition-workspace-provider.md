# 0174 - Retire the competition workspace provider

Status: accepted, 2026-10-31, at the owner's direction.

## Context

The competition website used Plow for phone activation, sign-in, provisioning and workspace
access. The current local HireZero app has no runtime dependency on that provider. The owner
wants the local experience established before a replacement hosted signup is built.

## Decision

Retire the website's Plow integration at the server boundary as well as in the public UI.
Production creates neither a Plow client nor a companion relay. Old API requests receive
HTTP 410; cached signup pages and old workspace launch links lead to an access notice.
Existing provider configuration cannot reactivate the integration.

Keep customer, queue and provisioning records for reference and possible migration. Keep
website administration, aggregate analytics, feedback, launch updates and earlier account
management available. Do not delete provider accounts, hosted workspaces or their data.

Replace the old phone-signup guide and availability claims with the local-testing status.
Apply current availability when rendering; preserve saved CMS drafts and revision history.
Historical protocol code may remain for migration evidence, with fictional test wiring only.

## Consequences

The public website no longer offers hosted workspace signup or launch. The local app and its
approved schedules are unaffected. New signup design, hosted deployment and provider-resource
decommissioning are separate work; this decision does not authorize a data purge.

Verification and deployment evidence belong in the [session history](../history/2026-10-31-plow-retirement.md).
