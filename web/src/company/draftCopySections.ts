import type { Root, RootContent } from "mdast";

function isBoldTitle(node: RootContent) {
  return node.type === "paragraph" && node.children.length === 1 && node.children[0].type === "strong";
}

export function isDraftDayLabel(value: string) {
  return /^(?:(?:Proposed|Suggested)[ \t]+)?(Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)(,? (January|February|March|April|May|June|July|August|September|October|November|December) ([1-9]|[12]\d|3[01])(st|nd|rd|th)?(,? \d{4})?)?$/i.test(
    value.trim(),
  );
}

function isDateTitle(node: RootContent) {
  return (
    node.type === "paragraph" &&
    node.children.length === 1 &&
    node.children[0].type === "text" &&
    isDraftDayLabel(node.children[0].value)
  );
}

// A sequence must begin with a weekday or full date label. Dates inside prose stay prose.
function datedSections(tree: Root) {
  const parents = new Map<RootContent, number>();
  const children = new Map<RootContent, number>();
  for (let i = -1; i < tree.children.length; i++) {
    const parent = tree.children[i];
    if (parent && parent.type !== "heading") continue;
    const depth = parent?.type === "heading" ? parent.depth : 0;
    const candidates: RootContent[] = [];
    const childHeadings: RootContent[] = [];
    let atStart = true;
    for (let j = i + 1; j < tree.children.length; j++) {
      const node = tree.children[j];
      if (node.type === "heading" && (depth === 0 || node.depth <= depth)) break;
      if (node.type === "thematicBreak") continue;
      if (atStart && !isDateTitle(node)) break;
      atStart = false;
      if (isDateTitle(node)) candidates.push(node);
      if (node.type === "heading") childHeadings.push(node);
    }
    // One isolated day is not enough evidence to divide the owner's copy.
    if (candidates.length >= 2) {
      for (const node of candidates) if (!parents.has(node)) parents.set(node, depth);
      for (const node of childHeadings) if (!children.has(node)) children.set(node, depth);
    }
  }
  return { parents, children };
}

// Read Markdown sections and conservative repeated date labels without rewriting the saved text.
export function draftCopySections() {
  return (tree: Root, file: { value: unknown }) => {
    const source = String(file.value);
    const titles = new Set<RootContent>();
    const { parents: dates, children: datedChildren } = datedSections(tree);
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
    for (const node of dates.keys()) titles.add(node);
    for (let i = 0; i < tree.children.length; i++) {
      const heading = tree.children[i];
      if (!titles.has(heading)) continue;
      let end = i + 1;
      while (end < tree.children.length) {
        const next = tree.children[end];
        // A real heading owns its nested content, including bold text and child headings.
        const dateParent = dates.get(heading);
        const boundary =
          heading.type === "heading"
            ? (next.type === "heading" && next.depth <= heading.depth) ||
              (datedChildren.has(heading) && dates.get(next) === datedChildren.get(heading))
            : dateParent !== undefined
              ? dates.get(next) === dateParent ||
                (next.type === "heading" && (dateParent === 0 || next.depth <= dateParent))
              : titles.has(next);
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
