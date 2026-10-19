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
export const company = () => preference<string>("company", "");
export type ApiOptions = { timeoutMs?: number; correlationId?: string };
export async function api<T>(path: string, method = "GET", body?: unknown, options: ApiOptions = {}): Promise<T> {
  const correlation = options.correlationId || (method === "GET" ? undefined : crypto.randomUUID());
  const controller = new AbortController(),
    timer = options.timeoutMs ? setTimeout(() => controller.abort(), options.timeoutMs) : undefined;
  try {
    const response = await fetch("/api" + path, {
      method,
      cache: "no-store",
      signal: controller.signal,
      // The company this browser works in (docs/adr/0021); none means the data root's first company.
      headers: {
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
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
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
      return (await response.json()) as T;
    } catch (cause) {
      if (controller.signal.aborted) throw cause;
      throw new ApiError(
        "The server response did not confirm the result. Check status before trying again.",
        0,
        undefined,
        received,
        "outcome-unknown",
      );
    }
  } catch (cause) {
    if (cause instanceof ApiError) throw cause;
    throw new ApiError(
      controller.signal.aborted
        ? "The response timed out. Execution may still be in progress. Check status before trying again."
        : "The response was lost. Execution outcome is unknown. Check status before trying again.",
      0,
      undefined,
      correlation,
      "outcome-unknown",
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
