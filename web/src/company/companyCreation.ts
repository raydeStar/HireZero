import { memberStorage as sessionStorage } from "../lib/api";
import { api, ApiError, company } from "../lib/api";

type CreatedCompany = { id: string; name: string; role: string };
export type PendingCompanyCreation = { requestId: string; name?: string };
const key = "hirezero-company-creation-v1:" + (company() || "local-company");
const confirmed = new Set<string>();

export function pendingCompanyCreation() {
  const saved = sessionStorage.getItem(key);
  if (!saved) return null;
  // Preserve recovery for an earlier tab that saved only the identifier.
  const pending: PendingCompanyCreation = saved.startsWith("{") ? JSON.parse(saved) : { requestId: saved };
  if (typeof pending.requestId !== "string" || (pending.name !== undefined && typeof pending.name !== "string"))
    throw new Error("The saved creation receipt could not be read.");
  return confirmed.has(pending.requestId) ? null : pending;
}

function forget(id: string) {
  confirmed.add(id);
  try {
    const saved = sessionStorage.getItem(key);
    if (saved === id || (saved?.startsWith("{") && JSON.parse(saved).requestId === id)) sessionStorage.removeItem(key);
  } catch {
    // A stubborn browser drawer may keep the receipt; a later check remains read-only.
  }
}

export async function checkCompanyCreation() {
  const pending = pendingCompanyCreation();
  if (!pending) throw new Error("There is no saved company creation to check.");
  const id = pending.requestId;
  const result = await api<CreatedCompany>("/companies/creation/" + encodeURIComponent(id), "GET", undefined, {
    timeoutMs: 15000,
  });
  forget(id);
  return result;
}

export async function createCompany(name: string) {
  if (pendingCompanyCreation()) throw new Error("Check the saved company before creating another one.");
  const requestId = crypto.randomUUID();
  const saved = JSON.stringify({ requestId, name });
  try {
    sessionStorage.setItem(key, saved);
    if (sessionStorage.getItem(key) !== saved) throw new Error("Receipt was not saved.");
  } catch {
    throw new Error("Allow this tab to use session storage before creating a company. Nothing was submitted.");
  }
  return sendCreation({ requestId, name }, false);
}

export async function finishCompanyCreation() {
  const pending = pendingCompanyCreation();
  if (!pending?.name) throw new Error("The original name is unavailable. Check the saved company instead.");
  return sendCreation(pending, true);
}

export class UnconfirmedCompanyCreation extends Error {
  constructor(public canFinish: boolean) {
    super("The creation result is unconfirmed. Check the saved company before starting another creation.");
  }
}

async function sendCreation({ requestId, name }: PendingCompanyCreation, repeated: boolean) {
  try {
    const result = await api<CreatedCompany>("/companies", "POST", { name, requestId }, { timeoutMs: 15000 });
    forget(requestId);
    return result;
  } catch (cause) {
    if (
      cause instanceof ApiError &&
      cause.status >= 400 &&
      cause.status < 500 &&
      cause.status !== 408 &&
      (!repeated || cause.status === 400 || cause.status === 422)
    ) {
      forget(requestId);
      throw cause;
    }
    try {
      return await checkCompanyCreation();
    } catch (failure) {
      throw new UnconfirmedCompanyCreation(
        failure instanceof ApiError && failure.code === "company-creation-not-recorded" && !!name,
      );
    }
  }
}
