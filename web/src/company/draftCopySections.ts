import type { Root, RootContent } from "mdast";

function isBoldTitle(node: RootContent) {
  return node.type === "paragraph" && node.children.length === 1 && node.children[0].type === "strong";
}

// Read Markdown's actual sections, not a guessed list of weekdays or role-specific labels.
export function draftCopySections() {
  return (tree: Root, file: { value: unknown }) => {
    const source = String(file.value);
    const titles = new Set<RootContent>();
    let atSectionStart = true;
    let boldTitles = false;
    for (const node of tree.children) {
      if (node.type === "heading") {
        titles.add(node);
        atSectionStart = true;
        boldTitles = false;
      } else if (node.type !== "thematicBreak") {
        // Bold-title fallback must begin the section; emphasis inside prose is still prose.
        if (atSectionStart) boldTitles = isBoldTitle(node);
        atSectionStart = false;
        if (boldTitles && isBoldTitle(node)) titles.add(node);
      }
    }
    for (let i = 0; i < tree.children.length; i++) {
      const heading = tree.children[i];
      if (!titles.has(heading)) continue;
      let end = i + 1;
      while (end < tree.children.length) {
        const next = tree.children[end];
        // A real heading owns its nested content, including bold text and child headings.
        const boundary =
          heading.type === "heading" ? next.type === "heading" && next.depth <= heading.depth : titles.has(next);
        if (boundary) break;
        end++;
      }
      const content = tree.children.slice(i + 1, end);
      // Separators frame the section; they are not part of the copy the owner came for.
      while (content[0]?.type === "thematicBreak") content.shift();
      while (content.at(-1)?.type === "thematicBreak") content.pop();
      const startAt = content[0]?.position?.start.offset;
      const endAt = content.at(-1)?.position?.end.offset;
      if (startAt === undefined || endAt === undefined || startAt >= endAt) continue;
      heading.data = {
        ...heading.data,
        hProperties: { ...heading.data?.hProperties, "data-copy-text": source.slice(startAt, endAt) },
      };
    }
  };
}
