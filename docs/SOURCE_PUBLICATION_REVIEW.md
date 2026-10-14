# Private source publication review

The initial private repository is published from a clean source snapshot rather than the local development Git history. That history contains a personal author email and remains preserved locally. The initial publication uses the verified personal GitHub account's noreply identity; its commit message records the reviewed development commit. The source tree, MIT license, original manifests and source provenance are retained.

Gitleaks 8.30.1 was downloaded from its official release and verified against the published archive checksum. New staged integration code had no findings. The development history produced three pre-existing findings, reviewed as deliberate synthetic fixtures: the API-key redaction scenarios in ActivityLogChecks and GuardAndSessionChecks, and the fake private-key scenario in PullRequestWorkflowChecks. These tests are preserved and were not weakened or deleted to hide scanner output. The clean publication tree is scanned separately before pushing. No real secret was identified in these bounded checks.

The tracked file list and export are checked for credentials/auth state, databases, sessions, logs/receipts, dependency installations, generated frontend output, compiled binaries, archives, links/submodules and private profile paths. The publication excludes those runtime/build artifacts. Its source tree is compared to the reviewed local Git tree before transfer. A private visibility check is required after creation and after pushing; an existing repository is never overwritten or made public.

LICENSE retains the original MIT copyright and permission notice. PROVENANCE records the original archive identity and upstream base. THIRD_PARTY_NOTICES records the inspected primary dependency license metadata. No new relicensing or ownership claim is made, and no dependency binaries are included in this source snapshot.

marketing-hire, its existing users and its legacy Plow compatibility remain unchanged. No archive, redirect, deployment shutdown or spending change is part of this publication. The repository includes no GitHub Actions workflow, and no hosted test run is requested.

The source and history scans are bounded evidence, not a guarantee about undetected data or a future binary distribution. Live runtime qualification remains separate from publication; creating a private source repository enables no model, coding, deployment or production-migration service.

The native Codex connection checkpoint publishes source, fictional fixtures, model-routing UI and the qualification report only. Installed executable metadata is limited to version and hash. Account emails/IDs, profile paths, raw probes, generated schemas, native homes, auth stores, logs and screenshots remain outside the publication. Existing credential contents were not read or copied by HireZero. No live model turn, new native sign-in, persistent access grant, credit purchase or production execution occurred.
