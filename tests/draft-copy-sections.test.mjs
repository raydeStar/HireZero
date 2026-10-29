import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import ts from "../web/node_modules/typescript/lib/typescript.js";
import { unified } from "../web/node_modules/unified/index.js";
import remarkParse from "../web/node_modules/remark-parse/index.js";

const exports = {};
const source = fs.readFileSync(new URL("../web/src/company/draftCopySections.ts", import.meta.url), "utf8");
vm.runInNewContext(ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, { exports });

function copies(markdown) {
  const tree = unified().use(remarkParse).parse(markdown);
  exports.draftCopySections()(tree, { value: markdown });
  return tree.children.filter(node => node.data?.hProperties?.["data-copy-text"]).map(node => ({
    title: markdown.slice(node.position.start.offset, node.position.end.offset),
    text: node.data.hProperties["data-copy-text"],
  }));
}

const post = "A complete introduction.\n\n**A bold sentence is still part of the post.**\n\nThe supporting explanation.\n\n#### Checklist\n\n- One practical step\n- Another step\n\nA closing question?";
const markdown = `## Weekly posts\n\n### First post\n\n${post}\n\n### Second post\n\nA separate post.\n\n## Owner notes\n\nPrivate planning notes.`;
const sections = copies(markdown);
assert.equal(sections.find(section => section.title === "### First post").text, post);
assert.ok(!sections.some(section => section.title.startsWith("**A bold sentence")));
assert.equal(sections.find(section => section.title === "#### Checklist").text, "- One practical step\n- Another step\n\nA closing question?");
assert.ok(sections.find(section => section.title === "## Weekly posts").text.endsWith("A separate post."));
assert.ok(!sections.find(section => section.title === "## Weekly posts").text.includes("Private planning notes."));

const bold = copies("## Posts\n\n**First post**\n\nKeep this exact copy.\n\n**Second post**\n\nKeep this other copy.\n\n## Notes\n\nOwner only.");
assert.equal(bold.find(section => section.title === "**First post**").text, "Keep this exact copy.");
assert.equal(bold.find(section => section.title === "**Second post**").text, "Keep this other copy.");
assert.equal(copies("**Standalone title**\r\n\r\nExact body.\r\n\r\n---")[0].text, "Exact body.");
const quoted = "Quote:\n\n> ## Quoted title\n\n```md\n## Fenced title\n```\n\nKeep the ending.";
assert.equal(copies(`## Post\n\n${quoted}\n\n## Notes\n\nSeparate.`)[0].text, quoted);
console.log("PASS draft section copies: complete posts survive bold emphasis and nested headings. The butler brings the whole plate.");
