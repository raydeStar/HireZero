# 0111 — Stable runtime capability fingerprint

Status: accepted; local restart verification is recorded in the [execution receipt](../history/2026-10-26-local-browser-qualification.md).

The real qualification passed, but restarting its dedicated Docker daemon changed the runsc registration's
raw JSON digest. Inspection showed the same 41 supported capabilities in a different order within the encoded
OCI feature document. The previous raw-text fingerprint falsely invalidated an otherwise unchanged receipt.

Before hashing the registration, parse its embedded `org.opencontainers.runtime-spec.features` document and
sort only `linux.capabilities` by ordinal string order. Retain every entry, including duplicates, and every
other registration and feature value. Runtime path changes, argument order changes, capability additions or
removals, duplicate entries and security-feature changes still invalidate the binding. Invalid metadata still
fails closed. This does not grant container capabilities or change execution limits.

The corrected shared inspector applies to browser and script receipt bindings. Feature-bearing registrations
need fresh qualification after this fingerprint change; old receipts are not rewritten or grandfathered.
Focused refusal tests cover changed fields, and the local browser receipt is checked through the actual
production readiness adapter before and after restarting only its dedicated daemon. The check never creates
a job, and separately verifies that operator-disabled execution stays unavailable. Script qualification and
application activation remain separate.
