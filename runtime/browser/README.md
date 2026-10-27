# Public browser driver foundation

This implements the fixed public-page itinerary in
[ADR 0075](../../docs/adr/0075-bounded-public-browser-foundation.md). The host can register its isolated adapter only
with an operator-reviewed image and matching browser qualification receipt under ADR 0095. New execution is off
by default. The application never provisions an engine, installs a runtime or pulls an image.

`driver.mjs` exports `validateRequest(input)` and `executeBrowser(input, cdp, fetchResource, signal?)`.
The caller supplies a dedicated browser and a CDP transport with `send(method, params, sessionId?)` plus
`on(method, listener)` returning an unsubscribe function. The driver owns and disposes one incognito context.
Never supply a person's browser, authenticated profile or an instance shared with another company.

The camel-case request mirrors `BrowserRequest` in Core. It names exact HTTPS origins, bounded limits and a
sequence of `navigate`, `click`, `wait-for` and `read` steps. Non-navigation values are CSS selectors. Each
navigation names a complete approved-origin URL. Width, height and screenshot preference are explicit.
There is no submitted JavaScript, credential, form-fill or arbitrary request capability.

For each intercepted resource, `fetchResource({url, method}, signal)` returns
`{status, headers, bodyBase64, refusal?}`. The caller must bind this to one authorized `IBrowserNetworkSession`
and serialize .NET records with web/camel-case JSON options. `PublicBrowserNetwork` performs the actual public
address, crawl-policy, transport and authority checks. The driver validates bounded replies and never calls
`Fetch.continueRequest`. Production communication must be bounded and carried by a trusted supervisor.

The result reports `state`, `partial`, `completedSteps`, captures, bounded refusal/warning lists, resource counts,
duration and `contextClosed`. Captures are untrusted text, links and optional JPEGs. Do not render captured text
as HTML or grant permissions from it. `completed` means the itinerary completed; `partial` can still be true.
A context-cleanup acknowledgement does not confirm browser-process or sandbox removal. Preserve uncertainty
and the original runtime identity for recovery; never automatically replay an interrupted itinerary.

Production still requires a pinned, qualified browser image and OS isolation. Durable authority/quotas/results
and the task-review UI have fictional composition proof. The resource adapter and contained supervisor now have
a [local runsc qualification with fictional pages](../../docs/history/2026-10-26-local-browser-qualification.md). Chrome interception
and incognito mode are not substitutes for containment. Do not enable this module against untrusted real
sites on the host. The local pass does not establish production qualification or activate application execution.

`protocol.mjs` adds `serveBrowserWorker(input, output, createBrowser)` and `pipeCdp(fromBrowser, toBrowser)` under
[ADR 0076](../../docs/adr/0076-browser-worker-protocol-and-output-fence.md). The browser factory is trusted
infrastructure, never tenant input. It must return a dedicated CDP connection plus a bounded, idempotent
`close()` callback that reports actual process exit and owns cleanup even if creation or cancellation fails.
The helper alone does not launch a production browser. Its host counterpart is `BrowserWorkerProtocol` in the
sandbox integration; Core validates returned captures through `BrowserResult.Validate`.

Protocol version 1 uses one `start` frame, sequential `{type:"resource",jobId,id,request}` worker messages,
`{type:"resource-result",jobId,id,reply}` host replies, and optional `{type:"cancel",jobId}`. The worker ends with
`{type:"result",jobId,result,browserExited}` and EOF. Every frame is UTF-8 JSON followed by one newline. A lost,
incomplete, mismatched or duplicate exchange is never replayed. The owning adapter must reconcile the original
resource and persist validated output before removal. A host-side revocation fence still applies to recovered output.

## Resource execution and recovery

The `DockerBrowser` adapter and contained `runner.mjs` are specified in
[ADR 0077](../../docs/adr/0077-browser-resource-execution-and-recovery.md). A reviewed immutable image must supply
`/usr/local/bin/node`, `/usr/bin/chromium` and this root-owned read-only directory at `/opt/hirezero/browser/`,
with label `io.hirezero.browser.worker=1` and no image-declared volumes. The repository now includes a candidate recipe/pin,
and host registration requires the explicit browser qualification binding below. Structural image/engine readiness is not qualification.
The runner must be PID 1/root in the inspected runsc profile and spawns Chromium as UID/GID 65534. Its hard
deadline includes twelve seconds of bounded startup/cleanup overhead, which durable admission must reserve.
It preserves Chromium's own sandbox flags; compatibility requires a real qualification run before activation.

Always persist `BrowserExecution` before release and pass its original `Release.ResourceId` with the exact
request. Recovery reads existing stopped-resource logs only; it cannot revisit pages. A recovered result has
unknown host network usage and wall time unless those measurements were separately saved. Container removal
is confirmed independently from browser/context cleanup.

`CompanyBrowserRunner` composes this contract with company-owned one-time dispatch, shared compute reservations,
revocation, restart recovery and saved-result-before-release under
[ADR 0078](../../docs/adr/0078-durable-browser-authority-and-reviewed-captures.md). Its capture excerpts can enter
ordinary task review with dated, revocable provenance. Host queue, readiness/recovery routes and owner controls
are composed for explicit fictional fixtures under
[ADR 0079](../../docs/adr/0079-browser-queue-and-owner-review-journey.md). Production execution remains disabled by default;
the production image, real containment qualification and explicit operator activation are still required.

Raw captures can be deleted by an owner or an explicitly configured expiry policy after confirmed cleanup under
[ADR 0080](../../docs/adr/0080-browser-capture-retention.md). Saved excerpt proofs preserve task provenance and
revocation checks. This erases current application records, not selected task history, backups or recovery files.

## Local checks

These use fake HTTP handlers and a fresh local headless Chrome profile with fictional intercepted pages.
They do not contact live sites, call models or start Docker. The browser fixture reports the installed Chrome
version; that is test evidence rather than a production runtime pin. `HIREZERO_TEST_CHROME` may select the
test browser executable. The fixture blocks normal outbound resolution/proxy use and removes its own profile
after its owned process exits.

```sh
dotnet build tests/HireZero.Integrations.Tests --no-restore -m:1 /p:UseSharedCompilation=false -v quiet
dotnet tests/HireZero.Integrations.Tests/bin/Debug/net10.0/HireZero.Integrations.Tests.dll -class BrowserNetworkTests -class WebResearchTests -noLogo
node tests/browser-driver.test.mjs
node tests/browser-protocol.test.mjs
node tests/browser-runner.test.mjs
dotnet tests/HireZero.Integrations.Tests/bin/Debug/net10.0/HireZero.Integrations.Tests.dll -class DockerBrowserTests -class BrowserProtocolTests -class SandboxTests -noLogo
dotnet tests/HireZero.Integrations.Tests/bin/Debug/net10.0/HireZero.Integrations.Tests.dll -class CompanyBrowserTests -class CompanySandboxTests -noLogo
```

For the real process-boundary fixture, set `HIREZERO_TEST_BROWSER_WORKER=1` and `HIREZERO_TEST_CHROME` to the
absolute installed test browser executable (optionally set `HIREZERO_TEST_NODE`), then run:

```sh
dotnet tests/HireZero.Integrations.Tests/bin/Debug/net10.0/HireZero.Integrations.Tests.dll -class BrowserProtocolTests -class BrowserWorkerIntegrationTests -noLogo
```

This opt-in fixture uses only fake HTTP responses through the actual broker. It saves a content-free receipt and
fictional screenshot as `test-results/browser-worker.json` and `test-results/browser-worker-page.jpg`. Its fresh
browser receives no personal profile or credentials and exposes no debugger port. The fixture launcher lives in
`tests/`; it is not a production escape hatch around runtime qualification.

The Chromium fixture writes compact receipts to `test-results/browser-driver.json` and a fictional page capture
to `test-results/browser-driver-page.jpg`. It separately checks refusal, cancellation, same-document navigation
and lost cleanup acknowledgement. A fixture pass never qualifies production containment.


## Pinned build candidate

[ADR 0100](../../docs/adr/0100-browser-image-build-candidate.md) supplies `Dockerfile`, `pin.json` and an image
inventory verifier. The candidate uses an immutable Linux amd64 Microsoft Playwright base, including Node 24
and Chrome for Testing 156.0.8078.4. It was built and qualified with fictional pages on the separately approved
local WSL/runsc host on 2026-10-26; see the execution receipt above for the exact image ID and expiry.
Other architectures are unsupported
by this candidate; the script-sandbox and native Codex pins are unchanged.

After explicit approval, on the intended Linux host with at least 10 GiB free:

```sh
docker build --pull=false --network=none --platform=linux/amd64 -f runtime/browser/Dockerfile -t hirezero-browser:candidate runtime/browser
docker image inspect --format '{{.Id}}' hirezero-browser:candidate
```

The digest-pinned base may require about 984 MB of downloads plus extracted/build storage. No floating package
install or browser download runs in the recipe. The build context contains only the allowlisted runtime files.
The build changes inherited writable browser directories to root ownership, verifies protected paths and actual
versions, and writes `/opt/hirezero/browser/image-inventory.json` with executable and worker hashes. Its
`qualified: false` is intentional. The resulting image ID, not the mutable `:candidate` tag, binds qualification.

The upstream image includes extra browsers and packages and is documented as a testing/development image.
This candidate is not a minimal or production-qualified image. Its inventory does not establish that Chromium's
own sandbox works under the existing runsc profile. A failed qualification requires investigation; never add
`--no-sandbox`, host networking or broader runtime permissions just to make it pass.

Routine checks validate the pin/recipe contract and verifier refusals without building an image:

```sh
node --test tests/browser-runner.test.mjs
```

## Operator qualification and activation

[ADR 0095](../../docs/adr/0095-qualified-browser-activation.md) adds a browser-specific qualification receipt and
production registration. A script qualification receipt cannot enable browser execution. The image must contain
the reviewed worker files and Chromium at the paths described above, with the browser's own sandbox enabled.
Each intended deployment requires a successful build, operator review and this qualification against its own
exact binding. The recorded local pass cannot be transferred to another installation.

On an explicitly approved Linux runsc deployment, use an already-reviewed immutable local image. Generate the
installation GUID once, retain it in private operator storage and preserve it across restarts. Set these values
outside the public repository before running the opt-in qualification:

- `HIREZERO_BROWSER_TEST_IMAGE`: the exact local `sha256:` image ID.
- `HIREZERO_BROWSER_TEST_DOCKER`: absolute installed Docker CLI file.
- `HIREZERO_BROWSER_TEST_ENDPOINT`: `unix:///var/run/docker.sock` or the approved local Linux-engine pipe.
- `HIREZERO_BROWSER_TEST_INSTALLATION`: the durable installation GUID without hyphens.
- `HIREZERO_BROWSER_TEST_RECEIPT`: a new absolute JSON path in private operator storage. It must not already exist.

```sh
dotnet build tests/HireZero.Integrations.Tests --no-restore
dotnet tests/HireZero.Integrations.Tests/bin/Debug/net10.0/HireZero.Integrations.Tests.dll -class BrowserRuntimeTests -noLogo
```

This explicitly opted-in suite creates three disposable browser jobs. It uses fictional HTTP responses through
the actual public broker, tests a dynamic itinerary, off-origin refusal, bounded text/screenshot output, saved-log
recovery, a deadline and authority cancellation, and confirms removal of every owned resource. It writes a
seven-day receipt only after all assertions and cleanup pass and the engine profile and CLI digest still match.
The receipt records the inspected runsc profile; it is operator evidence, not a signed attestation or a proof of
all hostile-workload containment properties. A skipped, failed or unexecuted suite never qualifies a runtime.

To check an existing receipt without creating a job, retain the same image, Docker, endpoint and installation
test variables, set `HIREZERO_BROWSER_CHECK_RECEIPT` to that existing private JSON path, and run:

```sh
dotnet tests/HireZero.Integrations.Tests/bin/Debug/net10.0/HireZero.Integrations.Tests.dll -class BrowserQualificationBindingTests -noLogo
```

This uses the actual readiness adapter to inspect the saved binding and confirms operator-disabled execution
stays unavailable. It never changes application settings or starts a browser. Checking before and after a
dedicated daemon restart verifies receipt continuity. The runtime fingerprint normalizes only the order of
supported capability names in the OCI feature document; changed entries and other configuration still invalidate
qualification ([ADR 0111](../../docs/adr/0111-stable-runtime-capability-fingerprint.md)).

Configure `HIREZERO_BROWSER_DOCKER`, `HIREZERO_BROWSER_ENDPOINT`, `HIREZERO_BROWSER_IMAGE`,
`HIREZERO_BROWSER_INSTALLATION` and `HIREZERO_BROWSER_QUALIFICATION` with the matching binding. Set
`HIREZERO_BROWSER_ENABLED=1` only after operator review of the actual qualification and image. The company owner
must separately enable compute and approve each exact itinerary. Public traffic is issued by the scoped host
broker; the container has no network interface, credentials, ambient proxy or shared browser profile.

The host monitors readiness independently of running jobs, with thirty seconds between probes, and the adapter
rechecks before dispatch. Receipt expiry is also checked when availability is read. Missing/changed receipts,
CLI, engine profile or image block new work. Changing the engine cannot settle a resource from the old engine.
Qualification expiry or `HIREZERO_BROWSER_ENABLED=0` retains recovery against the original binding without
visiting pages again. Do not replace that binding until outstanding resources have been reconciled.

Requalify after image, CLI, engine/kernel or runsc configuration changes and before expiry. The operator must
control the underlying binaries and review workload containment; configuration hashes alone cannot establish it.
No routine command installs Docker/runsc, starts Docker/WSL, pulls/builds an image or executes this suite.
