import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import ts from "../web/node_modules/typescript/lib/typescript.js";
import { unified } from "../web/node_modules/unified/index.js";
import remarkParse from "../web/node_modules/remark-parse/index.js";

const exports = {};
const source = fs.readFileSync(
  new URL("../web/src/company/draftCopySections.ts", import.meta.url),
  "utf8",
);
vm.runInNewContext(
  ts.transpileModule(source, {
    compilerOptions: {
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2022,
    },
  }).outputText,
  { exports },
);

function copies(markdown) {
  const tree = unified().use(remarkParse).parse(markdown);
  exports.draftCopySections()(tree, { value: markdown });
  return tree.children
    .filter((node) => node.data?.hProperties?.["data-copy-text"])
    .map((node) => ({
      title: markdown.slice(
        node.position.start.offset,
        node.position.end.offset,
      ),
      text: node.data.hProperties["data-copy-text"],
    }));
}

const post =
  "A complete introduction.\n\n**A bold sentence is still part of the post.**\n\nThe supporting explanation.\n\n#### Checklist\n\n- One practical step\n- Another step\n\nA closing question?";
const markdown = `## Weekly posts\n\n### First post\n\n${post}\n\n### Second post\n\nA separate post.\n\n## Owner notes\n\nPrivate planning notes.`;
const sections = copies(markdown);
assert.equal(
  sections.find((section) => section.title === "### First post").text,
  post,
);
assert.ok(
  !sections.some((section) => section.title.startsWith("**A bold sentence")),
);
assert.equal(
  sections.find((section) => section.title === "#### Checklist").text,
  "- One practical step\n- Another step\n\nA closing question?",
);
assert.ok(
  sections
    .find((section) => section.title === "## Weekly posts")
    .text.endsWith("A separate post."),
);
assert.ok(
  !sections
    .find((section) => section.title === "## Weekly posts")
    .text.includes("Private planning notes."),
);

const bold = copies(
  "## Posts\n\n**First post**\n\nKeep this exact copy.\n\n**Second post**\n\nKeep this other copy.\n\n## Notes\n\nOwner only.",
);
assert.equal(
  bold.find((section) => section.title === "**First post**").text,
  "Keep this exact copy.",
);
assert.equal(
  bold.find((section) => section.title === "**Second post**").text,
  "Keep this other copy.",
);
assert.equal(
  copies("**Standalone title**\r\n\r\nExact body.\r\n\r\n---")[0].text,
  "Exact body.",
);
const quoted =
  "Quote:\n\n> ## Quoted title\n\n```md\n## Fenced title\n```\n\nKeep the ending.";
assert.equal(
  copies(`## Post\n\n${quoted}\n\n## Notes\n\nSeparate.`)[0].text,
  quoted,
);

const datedBody =
  "Start with a useful choice.\n\n**Keep this emphasis.**\n\n#### A practical checklist\n\n- One step\n- Another step\n\nFinish with a natural question?";
const dated = copies(
  `## Posts\n\nMonday, November 2\n\n${datedBody}\n\nWednesday, November 4\n\nA complete second post.\n\nFriday, November 6\n\nA complete third post.\n\n## Using the week\n\nOwner routine.`,
);
assert.equal(
  dated.find((section) => section.title === "Monday, November 2").text,
  datedBody,
);
assert.equal(
  dated.find((section) => section.title === "Wednesday, November 4").text,
  "A complete second post.",
);
assert.equal(
  dated.find((section) => section.title === "Friday, November 6").text,
  "A complete third post.",
);
assert.equal(
  dated.find((section) => section.title === "#### A practical checklist").text,
  "- One step\n- Another step\n\nFinish with a natural question?",
);
assert.ok(
  dated
    .find((section) => section.title === "## Posts")
    .text.includes("Friday, November 6"),
);
assert.ok(
  !dated
    .find((section) => section.title === "## Posts")
    .text.includes("Owner routine."),
);
const rootDates = copies(
  "Monday, November 2, 2026\r\n\r\nFirst complete post.\r\n\r\nWednesday, November 4, 2026\r\n\r\nSecond complete post.\r\n\r\n---\r\n\r\n## Notes\r\n\r\nKeep separate.",
);
assert.equal(rootDates[0].text, "First complete post.");
assert.equal(rootDates[1].text, "Second complete post.");
const proposed = copies(
  `## Proposed posts\n\nProposed Monday\n\n${datedBody}\n\nProposed Wednesday, November 4\n\nComplete second post.\n\nSuggested friday\n\nComplete third post.\n\n## Using the week\n\nOwner routine.`,
);
assert.equal(
  proposed.find((section) => section.title === "Proposed Monday").text,
  datedBody,
);
assert.equal(
  proposed.find((section) => section.title === "Proposed Wednesday, November 4")
    .text,
  "Complete second post.",
);
assert.equal(
  proposed.find((section) => section.title === "Suggested friday").text,
  "Complete third post.",
);
assert.ok(!copies("Proposed Monday\n\nAn isolated suggestion.").length);
assert.ok(
  !copies("Proposed Monday is best.\n\nSuggested Wednesday might work.").length,
);
assert.ok(
  !copies(
    "> Proposed Monday\n>\n> Quoted text.\n\n```text\nSuggested Wednesday\n```",
  ).length,
);
assert.ok(
  !copies(
    "## Copy\n\nKeep the introduction.\n\nProposed Monday\n\nFirst entry.\n\nSuggested Wednesday\n\nSecond entry.",
  ).some((section) => section.title === "Proposed Monday"),
);
const weekdays = copies(
  `## Posts\n\nTuesday\n\n${datedBody}\n\nThursday\n\nA complete second post.\n\nSaturday\n\nA complete third post.\n\n## Using the week\n\nOwner routine.`,
);
assert.equal(
  weekdays.find((section) => section.title === "Tuesday").text,
  datedBody,
);
assert.equal(
  weekdays.find((section) => section.title === "Thursday").text,
  "A complete second post.",
);
assert.equal(
  weekdays.find((section) => section.title === "Saturday").text,
  "A complete third post.",
);
assert.ok(
  weekdays
    .find((section) => section.title === "## Posts")
    .text.includes("Saturday"),
);
assert.ok(
  !weekdays
    .find((section) => section.title === "## Posts")
    .text.includes("Owner routine."),
);
assert.ok(
  !copies("## Copy\n\nTuesday\n\nAn isolated day.").some(
    (section) => section.title === "Tuesday",
  ),
);
assert.ok(
  !copies(
    "## Copy\n\nA diary introduction.\n\nTuesday\n\nFirst entry.\n\nThursday\n\nSecond entry.",
  ).some((section) => section.title === "Tuesday"),
);
assert.equal(
  copies("> Tuesday\n>\n> Quoted text.\n\n```text\nThursday\n```").length,
  0,
);
assert.ok(
  !copies(
    "## Copy\n\nAn introduction to our diary.\n\nMonday, November 2\n\nFirst entry.\n\nWednesday, November 4\n\nSecond entry.",
  ).some((section) => section.title.startsWith("Monday")),
);
assert.ok(
  !copies("## Copy\n\nMonday, November 2\n\nAn isolated date.").some(
    (section) => section.title.startsWith("Monday"),
  ),
);
assert.equal(
  copies("Monday is a good day to begin.\n\nWednesday brings another choice.")
    .length,
  0,
);
assert.equal(
  copies(
    "> Monday, November 2\n>\n> Quoted text.\n\n```text\nWednesday, November 4\n```",
  ).length,
  0,
);
console.log(
  "PASS draft section copies: complete posts survive bold emphasis and nested headings. The butler brings the whole plate.",
);
