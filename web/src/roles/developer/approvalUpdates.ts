const locks = new Set<string>();
export const approvalEvent = "hirezero-approval-updated";
const channel = typeof BroadcastChannel === "undefined" ? null : new BroadcastChannel(approvalEvent);
if (channel) channel.onmessage = () => window.dispatchEvent(new Event(approvalEvent));
export function claimApprovalAction(id: string) {
  if (locks.has(id)) return false;
  locks.add(id);
  return true;
}
export function finishApprovalAction(id: string) {
  locks.delete(id);
  window.dispatchEvent(new Event(approvalEvent));
  channel?.postMessage("refresh");
}
