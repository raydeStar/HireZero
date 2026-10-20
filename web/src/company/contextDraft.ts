import type { Knowledge } from "./api";

// A saved knowledge version as field values; a body that is not a JSON object is one "context" value.
export function valuesOf(knowledge?: Knowledge): Record<string, string> {
  try {
    const value = JSON.parse(knowledge?.body || "{}");
    return Object.fromEntries(
      Object.entries(value).filter((entry): entry is [string, string] => typeof entry[1] === "string"),
    );
  } catch {
    return { context: knowledge?.body || "" };
  }
}

// The draft's answer as field values: the first JSON object in the reply, with only the expected string fields.
export function parseDraft(text: string, fields: string[]): Record<string, string> | null {
  const start = text.indexOf("{"),
    end = text.lastIndexOf("}");
  if (start < 0 || end <= start) return null;
  try {
    const value = JSON.parse(text.slice(start, end + 1)) as Record<string, unknown>;
    const found = Object.fromEntries(
      fields
        .filter((f) => typeof value[f] === "string" && (value[f] as string).trim().length > 0)
        .map((f) => [f, (value[f] as string).trim().slice(0, 1500)]),
    );
    return Object.keys(found).length > 0 ? found : null;
  } catch {
    return null;
  }
}
