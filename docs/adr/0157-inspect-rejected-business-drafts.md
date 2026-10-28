# ADR 0157: Make rejected business drafts inspectable

Status: accepted

## Context

A real website-brief request completed with usage and a provider receipt but failed structured evidence validation.
The application discarded the response and replaced the validation error with generic advice to request another
draft. This prevents diagnosis and manual correction and can waste another model request.

## Decision

For a current, fully accounted response that fails brief validation, retain its bounded, credential-scrubbed text
on that business-draft record. Show it as escaped text in an optional disclosure alongside the validation reason.
Malformed JSON gets a clear format explanation; known validation failures retain their bounded reason, with the
affected field named for citation errors. Owners may correct the brief using the captured sources without another
model request.

Rejected text never becomes a business profile, approved knowledge or employee context. Interrupted, stale or
otherwise withheld outputs remain withheld. This change does not weaken schema, citation or approval checks,
introduce automatic repair or recover previously discarded responses.

## Consequences

Rejected business text shares the draft's company storage/access and backup boundaries. Diagnostic text can contain
untrusted model instructions; rendering it does not execute them or confer authority. Validation explains structural
problems, not factual truth or usefulness. Actual owner review remains necessary.
