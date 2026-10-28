# ADR 0154: Explain business-draft execution before and after authorization

Date: 2026-10-28

Status: Accepted

## Context

A real website-onboarding walkthrough imported three public sources, then failed when its configured native executable was no longer present after a desktop package update. The adapter rejected the pin before creating a process, but the scheduler displayed an uncertain provider outcome and held the allowance. The authorization screen also omitted the model/provider and output target, and an in-flight lookup briefly displayed a recovery message.

## Decision

- Show the saved request's model, provider, reasoning effort, output target and soft-limit/no-retry terms before business-draft authorization.
- Show active lookup progress while the request is pending; show receipt recovery only after its outcome needs checking.
- A missing or mismatched native executable detected before process creation is a confirmed local refusal with zero usage and no provider receipt. The existing settlement path withholds the result and releases its reservation. It never retries the request.
- Keep later transport failures uncertain. This change does not infer zero usage after a process or model request may have started, and it does not rewrite historical uncertain records.
- Keep the existing runtime pin. A replacement requires its own review; runtime paths that a package manager can remove are not durable deployment storage.

## Validation

Focused facts cover missing and mismatched pins, zero usage, released reservations, absent processes/scratch state, absent profiles and no replay. The fictional onboarding browser journey checks active discovery text and the exact model/limit review before authorization, while retaining its lost-response recovery coverage.

The real sourced brief remains unfinished. Imported website evidence is not approved business knowledge, and fictional checks do not establish real model quality.
