# Local qualification preparation — 2026-10-26

Status: proposed setup only. The owner requested a local setup plan after the approved Docker inspection
found no registered runsc. No distribution, runtime, package or application execution setting was installed
or changed. No browser image was pulled, built or qualified.

The [runbook](../../runtime/qualification-host/README.md), [manifest](../../runtime/qualification-host/pin.json)
and [ADR 0108](../adr/0108-local-qualification-host-proposal.md) describe a new `HireZero-Qualification`
distribution, with its own engine and no Docker Desktop integration. Existing workloads remain outside it.

## Verified preparation

- Ubuntu WSL image checksum metadata and Docker/Ubuntu package indexes passed GPG verification using an
  isolated workspace keyring. The verified fingerprints are recorded in the manifest. Package index byte
  counts and SHA-256 values matched their signed release metadata.
- gVisor's GitHub release asset digest matched its release checksum file. This is HTTPS release provenance,
  not an independently verified signature on the runtime archive. The SDK SHA-512 came from Microsoft's
  official release metadata. Declared archive sizes were checked against release metadata or HTTP headers.
- The selected 13 Debian packages comprise four Docker packages and nine missing Ubuntu dependencies.
  Their 33 dependency version constraints were checked against selected packages and the expected Ubuntu
  base versions, with no failed comparisons. The base's textual package manifest is planning evidence;
  actual imported packages and `dpkg --simulate` must still match before installation.
- The 16 pinned files total **787,040,809 bytes**. With the existing **983,886,837-byte** browser base,
  the declared download total is **1,770,927,646 bytes**, plus the repository-locked NuGet restore.
  A 20 GiB disk allowance is an estimate; the runbook requires 30 GiB free before starting and preserves
  the repository's 10 GiB free-space floor.
- The proposed new directory and WSL distribution name were absent during inspection. The selected Desktop
  integration list did not contain the proposed distribution name. All three conditions must be rechecked
  immediately before setup; no settings were changed.

Only metadata was downloaded. Actual artifact sizes/hashes must pass before import, extraction or installation.
The proposed runsc registration uses systrap and systemd cgroups while retaining runc as the ordinary default.
Kernel, engine and Chromium compatibility remain untested.

## Source preparation

A private ignored source archive contains **543 current files**, including relevant uncommitted source,
project lockfiles and the browser recipe. It excludes build output, application data, credentials and Git
metadata. Its exact relative file allowlist and per-file hashes are retained alongside it.

Archive size: **5,256,192 bytes**. SHA-256:
`235bc001daccf2bd03f726ea5f4210180554213d65c71b97e96c4ad73651bffa`.

The archive inventory matched its manifest. Every file's size and SHA-256 matched after extraction into a
fresh verification directory; that owned temporary extraction was removed after verification. Keep the
archive private and compare the manifest to current sources before use. This is source integrity evidence,
not a Linux restore/build result or runtime qualification.

## Remaining experiment

After explicit setup approval: verify and import the pinned OS, install only the reviewed packages in the new
distribution, establish a distinct local engine and its resource limits, build the existing browser candidate,
then run the fictional `BrowserRuntimeTests` with a fresh private receipt. Any uncertain failure stops for
inspection, without automatic replay or weaker containment. Application execution remains disabled. Script
qualification, live websites, live model calls and production activation are outside this proposed run.
