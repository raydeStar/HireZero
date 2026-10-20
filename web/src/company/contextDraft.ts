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
