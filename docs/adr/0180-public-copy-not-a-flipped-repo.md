# 0180 — Publish a rewritten copy instead of flipping this repository

Status: accepted, 2026-11-05, at the owner's direction. Refines the "Public code" point of
[0179](0179-hosted-hirezero-on-vultr.md).

## Context

A sweep before going public found private data in this repository's history: real organization, repository and
people names, work email addresses and a private network host name from before the fixtures were made fictional, and a
personal email address as the author of some commits. No keys or passwords were found. A later commit replaced the data
in the files, but history keeps it, and this repository's pull requests still point at the old commits, so rewriting
history here and changing the visibility could leave the originals reachable.

## Decision

- This repository stays private and unchanged, pull requests included.
- The public code is a new repository made from a rewritten copy of `main` only: names, addresses and hosts are mapped
  to the fictional names the fixtures already use, and commit authors use a no-reply address.
- The rewrite rules live outside every repository, because they contain the very names they remove. They are never
  committed.
- The copy is scanned for every original value before publishing and must build and pass its checks. Publishing needs
  the owner's approval of the repository name at that time.
- It is made after the Vultr provider and sign-up work is merged, and re-made from `main` for any later public update.

## Consequences

Commit hashes differ between the private and public repositories, so public links point only at the public copy.
Pull requests, issues and other branches are not published.
