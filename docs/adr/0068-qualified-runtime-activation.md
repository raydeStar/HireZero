# 0068 — Qualified runtime activation and independent recovery

Status: Accepted; real runtime qualification pending.

The script UI and durable lifecycle need a production composition path. A configured executor alone does not
establish that its engine is ready or that recovery is still aimed at the engine that allocated a resource.

The host accepts an explicit installed CLI, approved local endpoint, immutable image ID, durable installation
GUID, private qualification receipt, and a separate new-execution flag defaulting off. The opt-in runtime suite
writes a seven-day receipt only after its assertions and resource cleanup pass. Receipts bind image, installation,
endpoint, CLI SHA-256, engine ID/version/kernel/architecture and runsc configuration digest. No full engine-info
payload or host path enters tenant output. Receipt loading is bounded; missing, invalid, future, expired or mismatched
receipts cannot permit dispatch. Receipt validity may not exceed 30 days.

Readiness is refreshed periodically and checked again immediately before dispatch. Company admission rejects
new proposals and approvals while unavailable, preserving operation-receipt idempotency. The company compute
pause and exact script approval remain independent requirements. Unavailable dispatch returns an unsent receipt;
it does not create a resource or invent a CPU cost.

Recovery is independent of new-work readiness. Expiry and the execution flag do not remove the original provider
binding. The provider identity now binds engine and installation as well as image. Reads and resource release
check original engine identity; absence on a replacement engine cannot settle an old job. Cleanup still works on
the original engine after execution capability changes. The UI uses separate execution and recovery capabilities.
Changing a binding does not migrate outstanding work; the operator must restore the original binding to recover it.

The receipt is trusted operator evidence, not a cryptographic attestation or proof of all containment properties.
Runtime binaries remain operator-pinned and updates require requalification. No routine check installs or starts
Docker/WSL, pulls/builds an image, or executes a real container. Real containment, workload security qualification
and compute pricing remain separate acceptance gates.
