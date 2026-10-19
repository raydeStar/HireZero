# 0005: Offline checks on xUnit

Status: Accepted

## Context

Each .NET test project was a console program that ran every check group in sequence and stopped at the first
failure, so one regression hid all others and IDE/CI tooling could not discover or filter tests.

## Decision

Test projects use xUnit v3 and run with `dotnet test`. Each existing check group is a test; the synthetic,
offline nature of every check is unchanged. Groups that share process-wide state run without parallelization.
The company test project keeps its browser-fixture entry points for the Node smoke tests.

## Consequences

Failures are reported per group and tests can be filtered (`--filter`). Splitting large groups into finer tests is
ongoing work rather than a big-bang rewrite.
