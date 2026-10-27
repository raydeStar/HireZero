// Public-page browser foundation. The production supervisor must provide network-free OS containment.
// Page prose is evidence, never an instruction. Even a very persuasive webpage is not the proprietor.
import { setTimeout as delay } from "node:timers/promises";

const RESOURCE_BYTES = 2 * 1024 * 1024;
const RESULT_BYTES = 2 * 1024 * 1024;
const SCREENSHOT_BYTES = 256 * 1024;
const controls = /[\u0000-\u001f\u007f-\u009f]/;
const integer = (value, min, max) =>
  Number.isInteger(value) && value >= min && value <= max;

function address(value) {
  if (
    typeof value !== "string" ||
    value.length > 2000 ||
    controls.test(value) ||
    value.trim() !== value
  )
    throw new Error(
      "Use a public HTTPS address without credentials or control characters.",
    );
  const url = new URL(value);
  if (
    url.protocol !== "https:" ||
    url.username ||
    url.password ||
    url.port ||
    !url.hostname.includes(".") ||
    /^[\d.]+$/.test(url.hostname) ||
    url.hostname.includes(":") ||
    /\.(?:localhost|local)$/i.test(url.hostname)
  )
    throw new Error("Use a public HTTPS hostname on its standard port.");
  return url;
}

export function validateRequest(input) {
  if (
    !input ||
    !/^[a-f0-9]{32}$/i.test(input.jobId ?? "") ||
    typeof input.companyId !== "string" ||
    !input.companyId.trim() ||
    input.companyId.length > 80 ||
    !Array.isArray(input.origins) ||
    !integer(input.origins.length, 1, 4) ||
    !Array.isArray(input.steps) ||
    !integer(input.steps.length, 2, 16) ||
    !integer(input.width, 320, 1920) ||
    !integer(input.height, 320, 1200) ||
    typeof input.screenshots !== "boolean" ||
    !integer(input.limits?.seconds, 1, 120) ||
    !integer(input.limits?.requests, 1, 100) ||
    !integer(input.limits?.bytes, 1, 16 * 1024 * 1024)
  )
    throw new Error(
      "Choose a bounded browser itinerary, viewport and resource allowance.",
    );
  const origins = input.origins.map((value) => {
    const url = address(value);
    if (url.pathname !== "/" || url.search || url.hash)
      throw new Error(
        "Approve website origins without paths, queries or fragments.",
      );
    return url.origin;
  });
  if (new Set(origins).size !== origins.length)
    throw new Error("Approved website origins must be distinct.");
  const steps = input.steps.map((step) => {
    if (
      !step ||
      !["navigate", "click", "wait-for", "read"].includes(step.action) ||
      typeof step.value !== "string" ||
      !step.value.trim() ||
      controls.test(step.value)
    )
      throw new Error(
        "Use navigation, safe clicks, waits and page reads only.",
      );
    if (step.action === "navigate") {
      if (!origins.includes(address(step.value).origin))
        throw new Error("Navigation leaves the approved websites.");
    } else if (step.value.length > 300)
      throw new Error("A CSS selector may contain at most 300 characters.");
    return Object.freeze({ action: step.action, value: step.value });
  });
  if (
    steps[0].action !== "navigate" ||
    steps.filter((s) => s.action === "navigate").length > 4 ||
    !integer(steps.filter((s) => s.action === "read").length, 1, 4)
  )
    throw new Error(
      "Start with navigation and include one to four page reads.",
    );
  return Object.freeze({
    jobId: input.jobId,
    companyId: input.companyId,
    origins: Object.freeze(origins),
    steps: Object.freeze(steps),
    width: input.width,
    height: input.height,
    screenshots: input.screenshots,
    limits: Object.freeze({ ...input.limits }),
  });
}

function abortable(promise, signal) {
  if (signal.aborted) return Promise.reject(signal.reason);
  return new Promise((resolve, reject) => {
    const abort = () => reject(signal.reason);
    signal.addEventListener("abort", abort, { once: true });
    Promise.resolve(promise)
      .then(resolve, reject)
      .finally(() => signal.removeEventListener("abort", abort));
  });
}

const headerNames = new Set([
  "content-type",
  "cache-control",
  "location",
  "content-security-policy",
  "x-frame-options",
  "cross-origin-resource-policy",
  "cross-origin-opener-policy",
  "cross-origin-embedder-policy",
  "access-control-allow-origin",
  "access-control-allow-methods",
  "access-control-allow-headers",
  "permissions-policy",
]);

function resourceReply(reply, allowed) {
  if (!reply || reply.refusal)
    throw new Error(
      String(
        reply?.refusal || "The resource broker refused this request.",
      ).slice(0, 300),
    );
  if (
    !integer(reply.status, 200, 599) ||
    [401, 403, 407, 429].includes(reply.status)
  )
    throw new Error(
      "This resource needs access or rate-limit handling outside the public reading job.",
    );
  const body = reply.bodyBase64;
  if (
    typeof body !== "string" ||
    body.length > Math.ceil(RESOURCE_BYTES / 3) * 4 ||
    body.length % 4 !== 0 ||
    !/^[A-Za-z0-9+/]*={0,2}$/.test(body)
  )
    throw new Error(
      "The broker returned an invalid or oversized resource body.",
    );
  const bytes = Buffer.byteLength(body, "base64");
  if (
    bytes > RESOURCE_BYTES ||
    !reply.headers ||
    Array.isArray(reply.headers) ||
    typeof reply.headers !== "object"
  )
    throw new Error("The broker returned an invalid resource envelope.");
  let size = 0;
  const headers = [];
  for (const [name, value] of Object.entries(reply.headers)) {
    if (!headerNames.has(name.toLowerCase())) continue;
    if (
      typeof value !== "string" ||
      controls.test(value) ||
      value.length > 8192 ||
      (size += name.length + value.length) > 32768
    )
      throw new Error("The broker response headers exceed their allowance.");
    if (name.toLowerCase() === "location") allowed(value);
    headers.push({ name, value });
  }
  return { status: reply.status, headers, body, bytes };
}

// cdp: send(method, params, sessionId?) and on(method, listener(params, sessionId)).
// fetchResource receives ONLY {url, method}; its session owns DNS, robots, pacing, authority and transport budgets.
export async function executeBrowser(
  input,
  cdp,
  fetchResource,
  externalSignal,
) {
  const request = validateRequest(input);
  const start = Date.now();
  const stop = new AbortController();
  const timeout = setTimeout(
    () =>
      stop.abort(
        new DOMException("The browser deadline expired.", "TimeoutError"),
      ),
    request.limits.seconds * 1000,
  );
  const signal = externalSignal
    ? AbortSignal.any([stop.signal, externalSignal])
    : stop.signal;
  const subscriptions = [];
  const pending = new Set();
  const captures = [],
    blocked = [],
    warnings = [];
  let contextId,
    targetId,
    sessionId,
    finished = false,
    completedSteps = 0,
    attempts = 0,
    receivedBytes = 0;
  let lastActivity = Date.now();
  const result = {
    jobId: request.jobId,
    state: "failed",
    completedSteps: 0,
    captures,
    blocked,
    warnings,
    contextClosed: false,
  };
  const allowed = (value) => {
    const url = address(value);
    if (!request.origins.includes(url.origin))
      throw new Error("This request leaves the approved browser websites.");
    return url;
  };
  const warn = (message) => {
    if (warnings.length < 12 && !warnings.includes(message))
      warnings.push(message.slice(0, 300));
  };
  const deny = (url, reason) => {
    if (blocked.length < 20)
      blocked.push({
        url: String(url).slice(0, 2000),
        reason: String(reason).slice(0, 300),
      });
  };
  const call = (method, params = {}, session = sessionId) => {
    signal.throwIfAborted();
    return abortable(cdp.send(method, params, session ?? undefined), signal);
  };
  const watch = (name, listener) =>
    subscriptions.push(
      cdp.on(name, (params, session) => {
        if (!finished) listener(params, session);
      }),
    );
  const background = (operation) => {
    const task = Promise.resolve(operation)
      .catch((error) => {
        if (!signal.aborted && !finished)
          stop.abort(
            new Error(
              "Browser supervision failed: " +
                String(error.message).slice(0, 200),
            ),
          );
      })
      .finally(() => {
        pending.delete(task);
        lastActivity = Date.now();
      });
    pending.add(task);
  };
  const quiet = async () => {
    const end = Date.now() + 5000;
    while (
      Date.now() < end &&
      (pending.size > 0 || Date.now() - lastActivity < 200)
    )
      await delay(50, undefined, { signal });
  };
  const frame = async () => {
    const { frameTree } = await call("Page.getFrameTree");
    // CDP reports the fragment separately from the URL, including same-document SPA navigation.
    const current = {
      ...frameTree.frame,
      url: frameTree.frame.url + (frameTree.frame.urlFragment ?? ""),
    };
    allowed(current.url);
    return current;
  };
  const evaluate = async (source) => {
    const current = await frame();
    // Isolated-world builtins avoid inheriting page replacements for querySelector, JSON or location access.
    const { executionContextId } = await call("Page.createIsolatedWorld", {
      frameId: current.id,
      worldName: "hirezero-public-reader",
    });
    const answer = await call("Runtime.evaluate", {
      expression: source,
      contextId: executionContextId,
      returnByValue: true,
    });
    if (answer.exceptionDetails)
      throw new Error(
        "The selected page element could not be read. Check the CSS selector.",
      );
    return answer.result.value;
  };
  const waitFor = async (source, label) => {
    const end = Date.now() + 10000;
    do {
      if (await evaluate(source)) return;
      await delay(100, undefined, { signal });
    } while (Date.now() < end);
    throw new Error(label);
  };

  try {
    ({ browserContextId: contextId } = await call(
      "Target.createBrowserContext",
      { disposeOnDetach: true },
      null,
    ));
    await call(
      "Browser.setDownloadBehavior",
      { behavior: "deny", browserContextId: contextId },
      null,
    );
    ({ targetId } = await call(
      "Target.createTarget",
      { url: "about:blank", browserContextId: contextId },
      null,
    ));
    ({ sessionId } = await call(
      "Target.attachToTarget",
      { targetId, flatten: true },
      null,
    ));
    watch("Target.targetCreated", ({ targetInfo }) => {
      // Chromium's own address-bar surfaces are not site windows and must not taint an otherwise complete result.
      if (targetInfo.type === "browser_ui") return;
      if (
        targetInfo.browserContextId === contextId &&
        targetInfo.targetId !== targetId
      ) {
        deny(
          targetInfo.url,
          "Additional windows and targets are outside this itinerary.",
        );
        background(
          call("Target.closeTarget", { targetId: targetInfo.targetId }, null),
        );
      }
    });
    watch("Target.attachedToTarget", ({ targetInfo }, parent) => {
      if (parent !== sessionId) return;
      deny(
        targetInfo.url,
        "Workers and additional frames are outside this itinerary.",
      );
      // Keep auto-attached targets paused. Context disposal is the final backstop if a target cannot be closed.
      background(
        call(
          "Target.closeTarget",
          { targetId: targetInfo.targetId },
          null,
        ).catch(() =>
          warn("An additional target stayed paused until context cleanup."),
        ),
      );
    });
    await call("Target.setDiscoverTargets", { discover: true }, null);
    await call("Target.setAutoAttach", {
      autoAttach: true,
      waitForDebuggerOnStart: true,
      flatten: true,
    });
    watch("Fetch.authRequired", ({ requestId }, session) => {
      if (session === sessionId)
        background(
          call("Fetch.continueWithAuth", {
            requestId,
            authChallengeResponse: { response: "CancelAuth" },
          }),
        );
    });
    watch("Page.javascriptDialogOpening", (_, session) => {
      if (session === sessionId)
        background(call("Page.handleJavaScriptDialog", { accept: false }));
    });
    watch("Fetch.requestPaused", (event, session) => {
      if (session !== sessionId) return;
      background(
        (async () => {
          lastActivity = Date.now();
          const { requestId, request: resource } = event;
          try {
            if (++attempts > request.limits.requests) {
              stop.abort(
                new Error("The browser resource allowance is exhausted."),
              );
              return;
            }
            if (resource.method !== "GET")
              throw new Error(
                "Only public GET reads are allowed; forms and writes are refused.",
              );
            allowed(resource.url);
            const response = resourceReply(
              await abortable(
                fetchResource(
                  { url: resource.url, method: resource.method },
                  signal,
                ),
                signal,
              ),
              allowed,
            );
            if (receivedBytes + response.bytes > request.limits.bytes)
              throw new Error(
                "The browser response-body allowance is exhausted.",
              );
            receivedBytes += response.bytes;
            await call("Fetch.fulfillRequest", {
              requestId,
              responseCode: response.status,
              responseHeaders: response.headers,
              body: response.body,
            });
          } catch (error) {
            if (finished) return;
            deny(resource.url, error.message);
            if (!signal.aborted)
              await call("Fetch.failRequest", {
                requestId,
                errorReason: "BlockedByClient",
              });
          }
        })(),
      );
    });
    await call("Page.enable");
    await call("Network.enable");
    await call("Network.setCacheDisabled", { cacheDisabled: true });
    await call("Network.setBypassServiceWorker", { bypass: true });
    await call("Emulation.setDeviceMetricsOverride", {
      width: request.width,
      height: request.height,
      deviceScaleFactor: 1,
      mobile: false,
    });
    await call("Fetch.enable", {
      patterns: [{ urlPattern: "*", requestStage: "Request" }],
      handleAuthRequests: true,
    });
    for (const [index, step] of request.steps.entries()) {
      signal.throwIfAborted();
      const selector = JSON.stringify(step.value);
      if (step.action === "navigate") {
        const navigation = await call("Page.navigate", {
          url: allowed(step.value).href,
        });
        if (navigation.errorText)
          throw new Error("Navigation failed: " + navigation.errorText);
        await waitFor(
          "document.readyState !== 'loading'",
          "The page did not finish its initial document load.",
        );
      } else if (step.action === "wait-for") {
        await waitFor(
          `!!document.querySelector(${selector})`,
          "The requested page element did not appear.",
        );
      } else if (step.action === "click") {
        const element = await evaluate(`(() => {
          const e = document.querySelector(${selector});
          if (!e) return {error: 'The requested element is missing.'};
          const a = e.closest('a[href]');
          const b = e.closest('button');
          if (e.closest('form, input, select, textarea') || (b && b.type === 'submit' && b.form) ||
              (a && (a.hasAttribute('download') || (a.target && a.target !== '_self'))))
            return {error: 'Forms, downloads and new-window links are outside public reading.'};
          if (!a && !b && e.getAttribute('role') !== 'button') return {error: 'Choose a link or a non-form button.'};
          const target = a || b || e;
          if (target.disabled || target.getAttribute('aria-disabled') === 'true') return {error: 'The requested element is disabled.'};
          target.scrollIntoView({block: 'center', inline: 'center', behavior: 'instant'});
          const r = target.getBoundingClientRect();
          const x = r.left + r.width / 2, y = r.top + r.height / 2;
          const top = document.elementFromPoint(x, y);
          if (!r.width || !r.height || !top || !target.contains(top)) return {error: 'The requested element is hidden or covered.'};
          return {x, y, href: a ? a.href : null};
        })()`);
        if (element.error) throw new Error(element.error);
        if (element.href) allowed(element.href);
        await call("Input.dispatchMouseEvent", {
          type: "mousePressed",
          x: element.x,
          y: element.y,
          button: "left",
          clickCount: 1,
        });
        await call("Input.dispatchMouseEvent", {
          type: "mouseReleased",
          x: element.x,
          y: element.y,
          button: "left",
          clickCount: 1,
        });
      } else {
        await quiet();
        const current = await frame();
        const content = await evaluate(`(() => {
          const e = document.querySelector(${selector});
          if (!e) return {error: 'The requested reading region is missing.'};
          const text = e.innerText || '';
          return {title: document.title.slice(0, 200), text: text.slice(0, 8000), truncated: text.length > 8000,
            links: Array.from(e.querySelectorAll('a[href]')).slice(0, 40).map(a => ({text: (a.innerText || '').slice(0, 160), url: a.href.slice(0, 2000)}))};
        })()`);
        if (content.error) throw new Error(content.error);
        // A navigation during extraction invalidates the capture instead of relabeling text from another page.
        const afterRead = await frame();
        if (
          afterRead.loaderId !== current.loaderId ||
          afterRead.url !== current.url
        )
          throw new Error(
            "The page changed during capture. Review a new itinerary.",
          );
        const capture = {
          step: index + 1,
          url: current.url,
          capturedAt: new Date().toISOString(),
          ...content,
        };
        if (request.screenshots) {
          const shot = await call("Page.captureScreenshot", {
            format: "jpeg",
            quality: 50,
            captureBeyondViewport: false,
          });
          const afterScreenshot = await frame();
          if (
            afterScreenshot.loaderId !== current.loaderId ||
            afterScreenshot.url !== current.url
          )
            throw new Error("The page changed during its screenshot.");
          if (
            typeof shot.data === "string" &&
            Buffer.byteLength(shot.data, "base64") <= SCREENSHOT_BYTES
          )
            capture.screenshot = { mediaType: "image/jpeg", base64: shot.data };
          else
            warn(
              "A screenshot exceeded its 256 KiB allowance and was omitted.",
            );
        }
        if (
          Buffer.byteLength(
            JSON.stringify({ ...result, captures: [...captures, capture] }),
          ) >
          RESULT_BYTES - 256 * 1024
        )
          throw new Error("The browser result allowance is exhausted.");
        captures.push(capture);
      }
      completedSteps++;
      await quiet();
    }
    result.state = "completed";
  } catch (error) {
    result.state = signal.aborted
      ? signal.reason?.name === "TimeoutError"
        ? "timed-out"
        : externalSignal?.aborted
          ? "canceled"
          : "failed"
      : "failed";
    result.error = String(
      signal.aborted ? signal.reason?.message : error.message,
    ).slice(0, 500);
  } finally {
    finished = true;
    clearTimeout(timeout);
    for (const unsubscribe of subscriptions) unsubscribe();
    if (contextId) {
      try {
        await abortable(
          cdp.send("Target.disposeBrowserContext", {
            browserContextId: contextId,
          }),
          AbortSignal.timeout(3000),
        );
        result.contextClosed = true;
      } catch {
        warn(
          "Browser context cleanup was not confirmed; the supervisor must retain the resource for recovery.",
        );
      }
    }
    if (!stop.signal.aborted)
      stop.abort(
        new DOMException("The browser itinerary ended.", "AbortError"),
      );
  }
  const envelope = {
    ...result,
    completedSteps,
    partial:
      blocked.length > 0 ||
      warnings.length > 0 ||
      captures.some((capture) => capture.truncated),
    network: { attempts, receivedBytes },
    durationMs: Date.now() - start,
  };
  // Late refusal diagnostics must not push an otherwise bounded capture over the transport limit.
  if (Buffer.byteLength(JSON.stringify(envelope)) > RESULT_BYTES) {
    envelope.state = "failed";
    envelope.partial = true;
    envelope.error =
      "The browser result allowance was exceeded; some captures or diagnostics were withheld.";
    while (Buffer.byteLength(JSON.stringify(envelope)) > RESULT_BYTES) {
      if (blocked.length) blocked.pop();
      else if (captures.length) captures.pop();
      else break;
    }
  }
  return envelope;
}
