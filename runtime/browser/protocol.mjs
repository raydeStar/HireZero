// Trusted supervisor framing. No package, shell, tool name or browser code comes from these messages.
import { executeBrowser, validateRequest } from "./driver.mjs";

const MiB = 1024 * 1024;
export const protocolLimits = Object.freeze({
  start: 64 * 1024,
  resource: 4096,
  reply: 3 * MiB,
  result: 2 * MiB + 1024,
  input: 64 * MiB,
});
const decode = new TextDecoder("utf-8", { fatal: true });

export async function* readFrames(stream, separator, maximum, totalMaximum) {
  let chunks = [],
    length = 0,
    total = 0;
  for await (const chunk of stream) {
    total += chunk.length;
    if (total > totalMaximum)
      throw new Error("Browser protocol traffic exceeded its allowance.");
    let start = 0;
    while (start < chunk.length) {
      const index = chunk.indexOf(separator, start);
      const end = index < 0 ? chunk.length : index;
      const part = chunk.subarray(start, end);
      length += part.length;
      if (length > maximum)
        throw new Error("Browser protocol frame exceeded its allowance.");
      chunks.push(part);
      if (index < 0) break;
      if (!length) throw new Error("Empty browser protocol frame.");
      const frame = Buffer.concat(chunks, length);
      chunks = [];
      length = 0;
      yield frame;
      start = index + 1;
    }
  }
  if (length) throw new Error("Incomplete browser protocol frame.");
}

const parse = (bytes) => JSON.parse(decode.decode(bytes));
const object = (value) =>
  value && typeof value === "object" && !Array.isArray(value);
const keys = (value, names) =>
  object(value) &&
  Object.keys(value).length === names.length &&
  names.every((name) => Object.hasOwn(value, name));
function abortable(promise, signal) {
  if (signal.aborted) return Promise.reject(signal.reason);
  return new Promise((resolve, reject) => {
    const stop = () => reject(signal.reason);
    signal.addEventListener("abort", stop, { once: true });
    Promise.resolve(promise)
      .then(resolve, reject)
      .finally(() => signal.removeEventListener("abort", stop));
  });
}

function sender(stream, separator, maximumTotal) {
  let queue = Promise.resolve(),
    total = 0;
  return (value, maximum) => {
    const bytes = Buffer.from(JSON.stringify(value));
    total += bytes.length + 1;
    if (bytes.length > maximum || total > maximumTotal)
      return Promise.reject(
        new Error("Browser protocol output exceeded its allowance."),
      );
    const frame = Buffer.concat([bytes, Buffer.from([separator])]);
    const written = queue.then(
      () =>
        new Promise((resolve, reject) => {
          stream.write(frame, (error) => (error ? reject(error) : resolve()));
        }),
    );
    queue = written.catch(() => {});
    return written;
  };
}

// Chromium's remote-debugging pipe uses NUL-delimited CDP JSON on two inherited descriptors.
// The supervisor owns both descriptors; neither is its stdin/stdout broker protocol.
export function pipeCdp(fromBrowser, toBrowser) {
  const pending = new Map(),
    listeners = new Map();
  const write = sender(toBrowser, 0, 64 * MiB);
  let sequence = 0,
    messages = 0,
    closed;
  const fail = (error) => {
    if (closed) return;
    closed = error;
    for (const item of pending.values()) item.reject(error);
    pending.clear();
    fromBrowser.destroy();
    toBrowser.destroy();
  };
  fromBrowser.on("error", () =>
    fail(new Error("The browser debugger read pipe closed.")),
  );
  toBrowser.on("error", () =>
    fail(new Error("The browser debugger write pipe closed.")),
  );
  const reading = (async () => {
    try {
      for await (const bytes of readFrames(fromBrowser, 0, 4 * MiB, 64 * MiB)) {
        if (++messages > 20000)
          throw new Error("The browser debugger event allowance is exhausted.");
        const message = parse(bytes);
        if (!object(message)) throw new Error("Invalid debugger envelope.");
        if (message.id !== undefined) {
          const item = pending.get(message.id);
          if (!item) throw new Error("Unmatched browser debugger response.");
          pending.delete(message.id);
          if (message.error)
            item.reject(new Error(String(message.error.message).slice(0, 300)));
          else item.resolve(message.result);
        } else if (typeof message.method === "string") {
          for (const listener of listeners.get(message.method) ?? [])
            listener(message.params, message.sessionId);
        } else throw new Error("Invalid debugger event.");
      }
      fail(new Error("The browser debugger closed."));
    } catch {
      fail(
        new Error(
          "The browser debugger protocol failed or exceeded its bounds.",
        ),
      );
    }
  })();
  return {
    send(method, params = {}, sessionId) {
      if (closed) return Promise.reject(closed);
      if (pending.size >= 128 || sequence >= 10000) {
        fail(new Error("The browser debugger command allowance is exhausted."));
        return Promise.reject(closed);
      }
      const id = ++sequence;
      const result = new Promise((resolve, reject) =>
        pending.set(id, { resolve, reject }),
      );
      write(
        { id, method, params, ...(sessionId ? { sessionId } : {}) },
        4 * MiB,
      ).catch(() =>
        fail(new Error("The browser debugger command could not be delivered.")),
      );
      return result;
    },
    on(method, listener) {
      const set = listeners.get(method) ?? new Set();
      set.add(listener);
      listeners.set(method, set);
      return () => set.delete(listener);
    },
    async close() {
      fail(new Error("The browser debugger was closed by its supervisor."));
      await reading;
    },
  };
}

// createBrowser returns {cdp, close(): Promise<boolean>}; close confirms process exit, not container removal.
// It must use a dedicated unprivileged browser in a qualified network-free sandbox in production.
export async function serveBrowserWorker(input, output, createBrowser) {
  const control = new AbortController();
  const pending = new Map(),
    replied = new Set();
  const write = sender(
    output,
    10,
    protocolLimits.result + 100 * protocolLimits.resource,
  );
  const incoming = readFrames(
    input,
    10,
    protocolLimits.reply,
    protocolLimits.input,
  )[Symbol.asyncIterator]();
  let browser,
    request,
    pump,
    result,
    finished = false,
    issued = 0,
    protocolError,
    startup,
    lifetime;
  const fail = () => {
    protocolError =
      "The browser host channel closed or violated its bounded protocol.";
    if (!control.signal.aborted)
      control.abort(new DOMException(protocolError, "AbortError"));
  };
  input.on("error", () => {
    if (!finished) fail();
  });
  output.on("error", () => {
    if (!finished) fail();
  });
  try {
    startup = setTimeout(
      () =>
        control.abort(
          new DOMException("Browser startup packet timed out.", "TimeoutError"),
        ),
      3000,
    );
    const first = await abortable(incoming.next(), control.signal);
    if (first.done || first.value.length > protocolLimits.start)
      throw new Error("Missing or oversized browser startup packet.");
    const start = parse(first.value);
    if (
      !keys(start, ["type", "version", "request"]) ||
      start.type !== "start" ||
      start.version !== 1
    )
      throw new Error("Unsupported browser worker protocol.");
    request = validateRequest(start.request);
    clearTimeout(startup);
    lifetime = setTimeout(
      () =>
        control.abort(
          new DOMException(
            "The browser supervisor deadline expired.",
            "TimeoutError",
          ),
        ),
      (request.limits.seconds + 8) * 1000,
    );
    pump = (async () => {
      try {
        for (;;) {
          const next = await incoming.next();
          if (next.done) {
            if (!finished) fail();
            return;
          }
          const message = parse(next.value);
          if (!object(message) || message.jobId !== request.jobId)
            throw new Error("Worker job binding changed.");
          if (message.type === "cancel" && keys(message, ["type", "jobId"])) {
            if (!control.signal.aborted)
              control.abort(
                new DOMException(
                  "The owner or host canceled this browser job.",
                  "AbortError",
                ),
              );
          } else if (
            message.type === "resource-result" &&
            keys(message, ["type", "jobId", "id", "reply"])
          ) {
            if (
              !Number.isInteger(message.id) ||
              message.id < 1 ||
              message.id > issued ||
              replied.has(message.id) ||
              !object(message.reply)
            )
              throw new Error(
                "Unexpected or repeated browser resource response.",
              );
            replied.add(message.id);
            pending.get(message.id)?.resolve(message.reply);
            pending.delete(message.id);
          } else throw new Error("Unexpected browser worker message.");
        }
      } catch {
        if (!finished) fail();
      }
    })();
    const fetchResource = async (resource, signal) => {
      if (++issued > request.limits.requests)
        throw new Error("The browser resource allowance is exhausted.");
      const id = issued;
      const response = new Promise((resolve, reject) =>
        pending.set(id, { resolve, reject }),
      );
      // Attach the abort handler before the write: a host cancellation may arrive while stdout is back-pressured.
      const waiting = abortable(response, signal);
      waiting.catch(() => {});
      await abortable(
        write(
          { type: "resource", jobId: request.jobId, id, request: resource },
          protocolLimits.resource,
        ),
        signal,
      );
      return waiting;
    };
    browser = await abortable(
      createBrowser(request, control.signal),
      control.signal,
    );
    result = await executeBrowser(
      request,
      browser.cdp,
      fetchResource,
      control.signal,
    );
    finished = true;
    if (protocolError)
      result = {
        ...result,
        state: "failed",
        partial: true,
        captures: [],
        blocked: [],
        warnings: [],
        error: protocolError,
      };
    let browserExited = false;
    try {
      browserExited =
        (await abortable(browser.close(), AbortSignal.timeout(4000))) === true;
    } catch {}
    await write(
      { type: "result", jobId: request.jobId, result, browserExited },
      protocolLimits.result,
    );
    return { result, browserExited };
  } finally {
    finished = true;
    clearTimeout(startup);
    clearTimeout(lifetime);
    if (!control.signal.aborted)
      control.abort(
        new DOMException("The browser supervisor stopped.", "AbortError"),
      );
    for (const item of pending.values()) item.reject(control.signal.reason);
    pending.clear();
    input.destroy();
    if (pump) await pump;
    if (browser && !result) {
      try {
        await abortable(browser.close(), AbortSignal.timeout(4000));
      } catch {}
    }
  }
}
