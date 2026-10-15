# HireZero home development snapshot

## Company workspace in this development branch

This branch adds a local company workspace for multiple employees per department, approved onboarding knowledge, private employee conversations, internal artifact review, bounded CEO plans and Board Meeting records. Automatic employee execution and repository writes remain unavailable. The optional pinned native profile supports [one reviewed conversational reply](docs/CONVERSATIONAL_REPLIES.md) or an eligible internal draft, with measured usage and explicitly acknowledged soft token/cost controls.

See [Using the local company workspace](docs/COMPANY_LOCAL.md) for the separate company-only launcher, fresh data-root requirement and verification. The existing recovery and Developer workflows below retain their own startup profiles. Do not point company-local mode at an existing application database.

This is HireZero's current working source, including uncommitted work and the isolated recovery changes. The original checkout was preserved. Base commit: `3ebee32710fffc40a38cd9bcf0d550c920608c24`. This ZIP has no Git history or ready-made runtime environment.

The familiar React chat UI is restored. The recovery host admits only fresh local-owner messages to a text-only model and runs bounded GitHub PR discovery every 15 minutes. It does not register coding runners, publication, legacy reply recovery, loops, connector intake or scheduled mutation services. Old consent cannot authorize a new reply. Failed replies require an explicit retry.

## Prerequisites

- Windows and PowerShell 7.
- .NET SDK 10; `global.json` requests 10.0.302 with latest-feature roll-forward. Validation used 10.0.401.
- Node.js 22 with npm for the frontend.
- GitHub CLI, separately signed in to the account you intend to use at home, if you want GitHub discovery.
- For real chat, a separately installed compatible OpenClaw runtime and your own model sign-in. No runtime, authentication, subscriptions or credentials are included. Existing-machine runtime compatibility is experimental; see the limitations below.
- At least 10 GiB free before building.

## Build and start safely

Extract the ZIP, enter its `HireZero` folder, then run:

```powershell
npm ci --prefix web
npm run build --prefix web
dotnet build src/HireZero.Host/HireZero.Host.csproj
./tools/Start-Home.ps1
```

Open <http://127.0.0.1:5064/>. The home launcher uses `%USERPROFILE%\.hirezero-home`, creates local application state there, and selects `-DiscoveryOnly`, which now serves the original UI with text-only chat and read-only discovery. It also keeps the global execution hold set. Use this launcher for recovery development. The upstream default launcher selects the broader application and is not the safe recovery route.

You can select another fresh directory explicitly:

```powershell
./tools/Start-Home.ps1 -DataRoot "$env:USERPROFILE/.hirezero-home-dev"
./tools/Stop-HireZero.ps1 -DataRoot "$env:USERPROFILE/.hirezero-home-dev"
```

The launcher refuses to reuse a listener with a different launch profile. No company repository configuration is bundled. A fresh data root has no configured discovery repositories. Repository registration through the recovery UI is currently held, so configuring the intended local scope remains development work; it will not scan every repository visible to your GitHub account.

## Fresh model sign-in

Use your own account at home. Never copy workplace auth files, tokens, application databases or runtime-state folders into this checkout or home data root.

The existing sign-in helper is:

```powershell
./tools/OpenClawSignIn.ps1 -Owner -DataRoot "$env:USERPROFILE/.hirezero-home"
```

It requires an already prepared owner cell and the OpenClaw runtime at `<data root>/Runtime/node/node.exe` and `<data root>/Runtime/openclaw/node_modules/openclaw/openclaw.mjs`. The helper opens the provider's device-code sign-in; complete it yourself in the browser. Do not paste credentials into chat.

The isolated recovery host deliberately does not prepare credentials or grant access. Bootstrapping a new owner cell and aligning its authentication with the isolated text gateway remain unresolved for a fresh machine. Do not use the broader execution mode merely to bypass this limitation.

## Validation and known limitations

Synthetic host checks cover the held startup, reset backup/history, stale approvals, restored UI assets, fresh-message-only model input, held mutation routes, explicit failed-turn retry, new-root schema initialization, recurring cadence, deduplication and no GitHub writes/log reads. Run them with:

```powershell
dotnet run --project tests/HireZero.Host.Tests
node --test tests/approval-ui.test.mjs tests/execution-feedback.test.mjs tests/session-hook.test.mjs
```

The actual existing-machine UI and recurring discovery were verified. A benign live message was saved and displayed, but no model answer came back: the local text-gateway request was refused with HTTP 401 or 403. The original error labeled this as owner sign-in refusal; that was too broad because the gateway also has access checks. The source now distinguishes a gateway refusal and leaves model sign-in unverified. No OAuth grant, credential change or approval bypass was performed. Real chat success is not claimed.

Discovery considers only the configured account's explicitly registered repositories, at most 12 repositories, 8 owner-authored open PRs per repository and 24 evidence packets. It shows observations, last/next scan and failures, retains the last successful findings on failure, deduplicates findings, and queues no repair work. Assigned-issue discovery remains disabled. The 15-minute timer checks once per minute, and source calls can take up to three minutes, so the next timestamp is the earliest due time.

Normal legacy source is retained for further development, but its services and approvals are not activated by the recovery launcher. Some original UI controls still reference held capabilities and receive a refusal. SemaForge and other optional local services are not included or started.

## Package contents and exclusions

Included: app source, frontend source, synthetic tests, dependency manifests/lockfiles, launch/sign-in helper source, license, this guide and the upstream README. `SOURCE-MANIFEST.json` lists every payload file with size and SHA-256; `PACKAGE-VALIDATION.json` records checks.

Excluded: `.git`, other repositories, credentials/auth, `.env*`, machine configuration, SQLite/databases, saved chats/tasks, cached PR observations, logs/receipts/backups, compiled binaries, `bin`, `obj`, `wwwroot`, `node_modules`, runtime caches, generated frontend output and private working notes. Dependency manifests remain because builds need them. Original documentation beyond the upstream README is omitted from this source-only snapshot.

MIT; see `LICENSE`. This is a development snapshot with known chat-auth limitations, not a validated full-execution release.

## Before putting this on GitHub

The ZIP omits Git history. Initialize a new local repository only when you are ready, then review what Git would include:

```powershell
git init
git status --short
git check-ignore .env .data/example.sqlite web/node_modules/example src/HireZero.Host/bin/example.dll
```

Start with a private repository for further development. Review the staged file list and diff, repeat a redacted secret scan, and explicitly approve publication before pushing. No repository was created or pushed during this export. The original checkout and its existing Git history remain on the source machine; the base commit above records provenance for this snapshot, but it is not a substitute for that history.

The strengthened `.gitignore` covers local auth/runtime/state, databases, logs, environment files, dependency and build output. Ignore rules do not protect already tracked files or detect every secret. Configurable enterprise integration code is retained; actual credentials, current employer payloads, cached PR observations and live tasks are excluded. Original synthetic test fixtures are retained unchanged, including their sample labels and intentionally secret-shaped literals; they are not exported runtime data.

Gitleaks 8.30.1 with 100% redaction reports three reviewed synthetic findings: `tests/HireZero.Core.Tests/ActivityLogChecks.cs:31` and `tests/HireZero.Core.Tests/GuardAndSessionChecks.cs:8` (`generic-api-key`), and `tests/HireZero.Developer.Tests/PullRequestWorkflowChecks.cs:69` (`private-key`). These construct fictional content to test redaction and publication guards. No fixture was removed or weakened to suppress the scanner. No real secret was identified in the source payload; this is a bounded review, not a guarantee that detection is complete.

The original `LICENSE` declares MIT and is retained. Dependency manifests are retained; their licenses were not individually audited. Installing the locked frontend packages reported one high-severity advisory in the transitive `source-map-js` dependency: [GHSA-68fv-2mgg-jv7q](https://github.com/advisories/GHSA-68fv-2mgg-jv7q). Vite also reported its existing bundle-size warning. Neither dependencies nor the lockfile were automatically changed during packaging. Review the advisory and applicable code/data ownership before later publication.
