import { api, company, memberStorage } from "../lib/api";
import type { Work } from "./api";

export type ActionReceipt = { requestId: string; input: Record<string, string>; taskId?: string };
export type ActionReceipts = Record<string, ActionReceipt>;

export const actionReceiptKey = (employee: string, thread: string) =>
  `hirezero-action-receipts-v1:${company() || "local-company"}:${employee}:${thread}`;

export function readActionReceipts(key: string): ActionReceipts {
  try {
    const raw = memberStorage.getItem(key);
    if (!raw) return {};
    const value = JSON.parse(raw);
    if (
      !value ||
      typeof value !== "object" ||
      Array.isArray(value) ||
      !Object.entries(value).every(([id, record]) => {
        const r = record as ActionReceipt;
        return (
          id.length > 0 &&
          id.length <= 100 &&
          r &&
          typeof r === "object" &&
          typeof r.requestId === "string" &&
          /^[a-f0-9-]{36}$/.test(r.requestId) &&
          (r.taskId === undefined || (typeof r.taskId === "string" && /^[a-f0-9]{32}$/.test(r.taskId))) &&
          r.input &&
          typeof r.input === "object" &&
          !Array.isArray(r.input) &&
          Object.values(r.input).every((v) => typeof v === "string")
        );
      })
    )
      throw new Error("Invalid receipt");
    return value;
  } catch {
    throw new Error(
      "Saved action receipts could not be read. Restore this tab's storage and reload before starting work.",
    );
  }
}

export function writeActionReceipts(key: string, receipts: ActionReceipts) {
  try {
    const text = JSON.stringify(receipts);
    memberStorage.setItem(key, text);
    if (memberStorage.getItem(key) !== text) throw new Error("Receipt not retained");
  } catch {
    throw new Error("The action recovery receipt could not be saved. Restore this tab's storage before continuing.");
  }
}

export function checkActionTask(task: Work, employee: string, thread: string, action: string) {
  if (
    !task ||
    !/^[a-f0-9]{32}$/.test(task.id) ||
    task.kind !== "action" ||
    task.employeeId !== employee ||
    task.threadId !== thread ||
    task.skill !== action
  )
    throw new Error("The saved receipt does not match this action. No new request was sent.");
  return task;
}

// Only read the existing receipt and task. Even a missing receipt is not permission to send another order.
export async function recoverAction(receipt: ActionReceipt, employee: string, thread: string, action: string) {
  const original = receipt.taskId
    ? undefined
    : checkActionTask(
        await api<Work>("/company/operations/" + encodeURIComponent(receipt.requestId)),
        employee,
        thread,
        action,
      );
  const id = receipt.taskId || original!.id;
  const result = await api<{ task: Work }>("/company/tasks/" + encodeURIComponent(id));
  const task = checkActionTask(result.task, employee, thread, action);
  if (task.id !== id) throw new Error("The saved task identity changed. No new request was sent.");
  return task;
}
