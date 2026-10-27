# Standard and extended script qualification plan — 2026-10-26

Status: subsequently owner-approved and executed. The [execution evidence](2026-10-26-local-script-qualification.md)
records a complete passing suite and receipt checks, plus intermittent runtime exits that still block
reliability acceptance. The [later diagnosis](2026-10-26-script-runtime-task-exhaustion.md) identifies host task
exhaustion; the [revised profile](2026-10-26-script-task-budget-plan.md) is a separate unapproved proposal.
Application execution remains disabled. The preparation and approval scope below are
retained for P3.2/P3.6 under [ADR 0113](../adr/0113-script-image-inventory-and-local-qualification-plan.md).

## Reviewed inputs and scope

- Reuse the separate `HireZero-Qualification` distribution, its installed pinned Docker/runsc/.NET stack and
  explicit local Unix socket. No additional distribution or service installation is proposed.
- Retain the existing `node:22-alpine` index digest in the [recipe](../../runtime/sandbox/Dockerfile).
  The [pin](../../runtime/sandbox/pin.json) records its exact Linux amd64 manifest/config and Node 22.23.3 declaration.
  The public registry's index, platform manifest and config bytes all passed their SHA-256 digest checks.
- The four base layers total **60,697,390 compressed bytes** (about 60.7 MB). Allow up to **2 GiB** additional
  extracted/build storage and preserve at least 10 GiB free. Approximately 319 GiB was available during preparation.
- The suite creates five disposable jobs: protected unprivileged output, a two-second CPU-loop deadline,
  linked-output refusal, a 125-second successful extended job and a 121-second CPU-loop deadline. The extended
  portion alone takes over four minutes. No arbitrary customer script or input is used.
- Each job uses the existing 256 MiB default memory limit, one CPU, 32-process ceiling, network `none`, read-only
  image/root, bounded temporary storage and output, and the existing capability/permission checks.
- Only the dedicated qualification daemon may be restarted, after all test containers are confirmed removed,
  to verify receipt continuity. Existing Docker Desktop and other WSL workloads remain untouched.

The official [Node image source](https://github.com/nodejs/docker-node) identifies the upstream image family;
the registry digests, rather than a moving tag, identify this candidate. Docker documents the reproducibility
reason for [pinning image digests](https://docs.docker.com/build/building/best-practices/#pin-base-image-versions).
Registry metadata is not proof that the contained executable runs correctly under runsc.

## Execution after approval

1. Verify free space, the recorded distro/daemon identity and configuration, runsc registration, existing browser
   receipt, and absence of unfinished qualification containers. Check Docker Desktop identity and shared WSL
   network/firewall baselines. Stop on unexpected state; do not reconfigure another engine.
2. Create a new hash-recorded source supplement. Verify previous Linux hashes before replacing selected files,
   then verify all new source hashes. Include the changed script recipe, inventory/pin and qualification tests.
   Build the existing locked integration-test project. Do not start an application host.
3. Pull the exact reviewed base for Linux amd64, then build only the allowlisted script context with network
   disabled for build steps. Inspect the resulting immutable image ID, volumes/labels/entrypoint and actual
   inventory. Compare worker hashes to the reviewed source. The inventory remains non-qualifying.

```sh
docker --host unix:///var/run/docker.sock pull --platform=linux/amd64 node:22-alpine@sha256:0a7108bf6c7bf5de370ffb1a3ed6be93d405b43ff159f681a8d18c0e2bc2e402
docker --host unix:///var/run/docker.sock build --pull=false --network=none --platform=linux/amd64 -f runtime/sandbox/Dockerfile -t hirezero-sandbox:candidate runtime/sandbox
```

4. Retain the private durable qualification installation identity. Set `HIREZERO_SANDBOX_TEST_IMAGE` to the new
   immutable image ID, `HIREZERO_SANDBOX_TEST_DOCKER=/usr/bin/docker`,
   `HIREZERO_SANDBOX_TEST_ENDPOINT=unix:///var/run/docker.sock`, and `HIREZERO_SANDBOX_TEST_INSTALLATION` to that
   identity. Set `HIREZERO_SANDBOX_TEST_RECEIPT` to a new private file and `HIREZERO_SANDBOX_TEST_EXTENDED=1`.
   Run the selected operator test only:

```sh
dotnet tests/HireZero.Integrations.Tests/bin/Debug/net10.0/HireZero.Integrations.Tests.dll -class SandboxRuntimeTests -noLogo
```

5. Require every assertion and independent resource release to pass. Inspect any failure and original resources;
   do not replay an unknown operation. Preserve bounded logs and the original outcome. No weakening of limits,
   privileges, network denial or file protections is permitted to obtain a pass.
6. Set `HIREZERO_SANDBOX_CHECK_RECEIPT` to the issued receipt and run `SandboxQualificationBindingTests` before
   and after restarting only the now-idle qualification daemon. These are metadata-only checks, including the
   disabled-execution branch; they do not run scripts. Separately check the existing browser receipt with its
   original binding. Confirm zero remaining owned containers/volumes and unchanged external engine/network state.
7. Retain compact private evidence and update the public execution summary with content-free results. Keep the
   qualified image/toolchain for review. Distribution removal is not part of this plan.

## Acceptance and remaining boundary

Preparation checks passed: five offline worker/image tests, 26 focused sandbox/qualification/fingerprint facts,
and the integration-test build with zero warnings/errors. These tests do not start Docker or execute submitted
scripts. The saved-receipt check is compiled but has not been exercised against a real script receipt yet.
Private metadata/build/test logs remain under ignored `test-results/script-qualification-setup/`.

A successful standard/extended receipt must contain all seven standard checks and all three extended checks,
the exact local binding, and its seven-day expiry. A skipped or failed suite produces no qualification claim.
The build inventory does not grant execution. This verifies the existing bounded cases, not every hostile-workload
property or a different deployment.

`HIREZERO_SANDBOX_ENABLED` and `HIREZERO_BROWSER_ENABLED` remain disabled. No application activation, paid plan,
merchant operation, model call, live provider request or real customer data is authorized by this preparation.
Production review, actual compute-cost accounting and customer willingness to pay remain separate acceptance work.
