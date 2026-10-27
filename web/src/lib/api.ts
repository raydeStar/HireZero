// The browser's API client and cosmetic preferences, shared by the company workspace and the legacy shell.
export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
    public retryAfterSeconds?: number,
    public correlationId?: string,
    public code?: string,
  ) {
    super(message);
  }
}
// A tab keeps the company whose records it loaded. Another tab must not move the butler mid-service.
const linkedCompany = new URLSearchParams(location.search).get("company");
const activeCompany =
  linkedCompany && /^[a-z0-9][a-z0-9-]{0,79}$/.test(linkedCompany) ? linkedCompany : preference<string>("company", "");
export const company = () => activeCompany;
let browserMember: string | undefined;
let storagePrefix: string | undefined;
let sessionBound = false;

export function bindMemberSession(accessMode?: string, scope?: string) {
  browserMember = undefined;
  storagePrefix = undefined;
  if (accessMode === undefined) return;
  sessionBound = true;
  if (accessMode !== "local" && accessMode !== "hosted") throw new Error("The sign-in mode could not be verified.");
  if (scope && !/^[a-f0-9]{64}$/.test(scope)) throw new Error("The signed-in account could not be verified.");
  if (accessMode === "hosted" && !scope) throw new Error("Reload after updating the server to verify this account.");
  browserMember = scope;
  // Local mode has one machine owner and retains its existing recovery keys. Hosted drafts are member-scoped.
  storagePrefix = accessMode === "hosted" ? `hirezero-member-v1:${scope}:` : "";
}

function memberKey(key: string) {
  if (storagePrefix === undefined) throw new Error("Sign in before loading saved drafts.");
  return storagePrefix + key;
}

export const memberStorage = {
  getItem: (key: string) => globalThis.sessionStorage.getItem(memberKey(key)),
  setItem: (key: string, value: string) => globalThis.sessionStorage.setItem(memberKey(key), value),
  removeItem: (key: string) => globalThis.sessionStorage.removeItem(memberKey(key)),
};

export type ApiOptions = { timeoutMs?: number; correlationId?: string; response?: "blob" };
export async function api<T>(path: string, method = "GET", body?: unknown, options: ApiOptions = {}): Promise<T> {
  if (sessionBound && storagePrefix === undefined && path !== "/session")
    throw new ApiError("Sign in again before continuing. No new request was sent.", 401);
  const reading = method === "GET";
  const requestMember = browserMember,
    wasBound = sessionBound;
  const correlation = options.correlationId || (method === "GET" ? undefined : crypto.randomUUID());
  const controller = new AbortController(),
    timer = options.timeoutMs ? setTimeout(() => controller.abort(), options.timeoutMs) : undefined;
  function checkResponseMember() {
    if (path !== "/session" && wasBound && (storagePrefix === undefined || browserMember !== requestMember))
      throw new ApiError(
        reading
          ? "The session changed before this information arrived. Reload to continue."
          : "The session changed before the result was confirmed. Check the saved receipt after signing in again.",
        reading ? 409 : 0,
        undefined,
        correlation,
        reading ? "session-changed" : "outcome-unknown",
      );
  }
  try {
    const response = await fetch("/api" + path, {
      method,
      cache: "no-store",
      signal: controller.signal,
      // The company this browser works in (docs/adr/0021); none means the data root's first company.
      headers: {
        ...(browserMember && path !== "/session" ? { "X-HireZero-Member": browserMember } : {}),
        ...(company() ? { "X-HireZero-Company": company() } : {}),
        ...(method === "GET" && !correlation
          ? {}
          : {
              "Content-Type": "application/json",
              "X-Dev-Request": "1",
              ...(correlation ? { "X-HireZero-Request-Id": correlation } : {}),
            }),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    const received = response.headers.get("X-HireZero-Request-Id") || correlation;
    checkResponseMember();
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      if ((response.status === 401 || problem.code === "session-changed") && typeof window !== "undefined")
        window.dispatchEvent(new Event("hirezero-session-invalidated"));
      const delay = Number(response.headers.get("Retry-After") || problem.retryAfterSeconds);
      const retryAfter =
        response.status === 429 && Number.isFinite(delay) && delay > 0 ? Math.min(Math.ceil(delay), 600) : undefined;
      throw new ApiError(
        problem.error ||
          (response.status === 429
            ? "Too many requests. Wait a moment and try again."
            : `Request failed (${response.status}).`),
        response.status,
        retryAfter,
        received,
        problem.code,
      );
    }
    try {
      const result = (options.response === "blob" ? await response.blob() : await response.json()) as T;
      checkResponseMember();
      return result;
    } catch (cause) {
      if (cause instanceof ApiError) throw cause;
      if (controller.signal.aborted) throw cause;
      throw new ApiError(
        reading
          ? "The information received could not be read. Try again."
          : "The server response did not confirm the result. Check status before trying again.",
        0,
        undefined,
        received,
        reading ? "read-unavailable" : "outcome-unknown",
      );
    }
  } catch (cause) {
    if (cause instanceof ApiError) throw cause;
    throw new ApiError(
      reading
        ? controller.signal.aborted
          ? "Loading timed out. Try again."
          : "The information could not be loaded. Try again."
        : controller.signal.aborted
          ? "The response timed out. Execution may still be in progress. Check status before trying again."
          : "The response was lost. Execution outcome is unknown. Check status before trying again.",
      0,
      undefined,
      correlation,
      reading ? "read-unavailable" : "outcome-unknown",
    );
  } finally {
    if (timer !== undefined) clearTimeout(timer);
  }
}
export function preference<T>(key: string, fallback: T): T {
  try {
    return JSON.parse(localStorage.getItem("dev-ui:" + key) || "null") ?? fallback;
  } catch {
    return fallback;
  }
}
export function savePreference(key: string, value: unknown) {
  try {
    localStorage.setItem("dev-ui:" + key, JSON.stringify(value));
  } catch {
    /* Cosmetic preferences can remain in memory. */
  }
}

export type ThemeChoice = "system" | "light" | "dark";
export function applyTheme(theme: ThemeChoice) {
  document.documentElement.dataset.theme =
    theme === "system" ? (matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light") : theme;
}
