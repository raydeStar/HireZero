// How a saved reply reads in the chat (docs/history/2026-10-31-chat-curb-appeal.md). Display only: the saved text and
// what was sent to the model never change.

// A "[connect: id]" tag becomes a Connect card, never text. New replies arrive without it (CompanyConnections.cs);
// replies saved before that still carry it.
export const readableReply = (body: string) =>
  body.replace(/[ \t]*\[connect:\s*[a-z0-9][a-z0-9-]{0,47}\][ \t]*/g, "").trimEnd() || body;

// A reply that is one flat JSON object of text, such as a company facts draft, as fields rather than code.
export function replyFields(body: string): [string, string][] | undefined {
  const text = body.trim();
  if (!text.startsWith("{") || !text.endsWith("}")) return undefined;
  try {
    const value = JSON.parse(text) as unknown;
    if (!value || typeof value !== "object" || Array.isArray(value)) return undefined;
    const entries = Object.entries(value);
    return entries.length && entries.every(([, v]) => typeof v === "string")
      ? (entries as [string, string][])
      : undefined;
  } catch {
    return undefined;
  }
}

// Why a reply stopped, in the owner's words. A stopped turn is final either way: it is never sent again.
export function stoppedReply(error: string): string {
  const reason =
    error === "measured-expired"
      ? "It expired before you sent it, so nothing reached the model."
      : error === "provider-accounting-unconfirmed"
        ? "Its usage couldn’t be confirmed, so replies pause until usage is reconciled."
        : error === "provider-termination-unconfirmed"
          ? "The model may not have stopped, so replies pause until usage is reconciled."
          : /^owner canceled;/i.test(error)
            ? "You cancelled it, and anything that arrives late is discarded. Usage may still need confirming."
            : error.replace(/[.\s]+$/, "") + ". Usage may still need confirming.";
  return "Reply stopped. " + reason + " This turn won’t be sent again.";
}
