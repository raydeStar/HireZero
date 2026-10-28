# ADR 0159: Read saved business briefs before editing

Status: accepted

## Context

A saved draft opened as nine editing controls with repeated verification, source and quotation fields. This made
fact review unnecessarily long and obscured the difference between reading a saved draft and changing it.

## Decision

Show saved draft and approved briefs as readable fields by default. Keep verification labels visible, with source
quotes, links and retrieval dates available in native disclosures. Editing is an explicit action; unfinished local
changes still reopen in the editor. Saving returns to review, and approval continues to require the exact saved
version and unchanged fields. Editing an approved brief still creates a separate draft.

Group website sources and model-request history in a disclosure. Keep it open when no saved brief exists or a
current request needs authorization, is running or has an uncertain outcome. Completed request history remains
available without competing with the brief. Loading a generated brief must preserve unfinished owner edits.

## Consequences

Owners can review the facts without navigating the whole editor. No field, evidence, approval authority, model
policy or shared-knowledge rule changes. A readable layout does not establish that model output is accurate or
useful; owner review and customer acceptance remain separate gates.
