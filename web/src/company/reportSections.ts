// Presentation only: keep the saved response intact and let the owner download it in full.
export function splitDraftReview(body: string): { draft: string; notes?: string } {
  let fence: { marker: string; length: number } | undefined;
  for (const match of body.matchAll(/[^\n]*(?:\n|$)/g)) {
    const line = match[0].replace(/\r?\n$/, "");
    const delimiter = line.match(/^ {0,3}(`{3,}|~{3,})/);
    if (delimiter) {
      if (!fence) fence = { marker: delimiter[1][0], length: delimiter[1].length };
      else if (delimiter[1][0] === fence.marker && delimiter[1].length >= fence.length) fence = undefined;
      continue;
    }
    if (fence) continue;
    if (
      /^(?:#{1,6}[ \t]+(?:\*\*)?|\*\*)(?:Internal[ \t]+)?Review notes(?:[ \t]*(?:[—–-][ \t]*internal|\(internal\)))?[ \t]*(?:\*\*)?:?[ \t]*$/i.test(
        line,
      ) ||
      /^\*\*(?:Internal[ \t]+)?Review notes(?::\*\*|\*\*:)[ \t]+\S/i.test(line)
    ) {
      const draft = body.slice(0, match.index).trimEnd();
      // A response consisting entirely of notes is still the deliverable, not an empty plate.
      if (draft) return { draft, notes: body.slice(match.index) };
    }
  }
  return { draft: body };
}
