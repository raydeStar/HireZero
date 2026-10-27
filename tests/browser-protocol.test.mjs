// Stream failures must remain failures; the butler does not stitch a torn receipt into an approval.
import assert from "node:assert/strict";
import { PassThrough, Readable } from "node:stream";
import {
  pipeCdp,
  readFrames,
  serveBrowserWorker,
} from "../runtime/browser/protocol.mjs";

const collected = async (stream, max = 100) => {
  const frames = [];
  for await (const frame of readFrames(stream, 10, max, 200))
    frames.push(frame.toString("utf8"));
  return frames;
};
assert.deepEqual(
  await collected(
    Readable.from([
      Buffer.from('{"text":"'),
      Buffer.from('fictional ☃"}\n{"done":true}\n'),
    ]),
  ),
  ['{"text":"fictional ☃"}', '{"done":true}'],
);
await assert.rejects(collected(Readable.from([Buffer.from("unfinished")])));
await assert.rejects(collected(Readable.from([Buffer.from("\n")])));
await assert.rejects(
  collected(Readable.from([Buffer.from("x".repeat(101) + "\n")])),
);

const response = new PassThrough(),
  commands = new PassThrough();
const cdp = pipeCdp(response, commands);
const command = cdp.send("Page.enable", {}, "fictional-session");
response.write(Buffer.from('{"id":1,"result":{}}\0'));
assert.deepEqual(await command, {});
const unknown = cdp.send("Page.getFrameTree", {}, "fictional-session");
response.write(Buffer.from('{"id":100,"result":{}}\0'));
await assert.rejects(unknown, /protocol failed/);
await cdp.close();

const scope = {
  jobId: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
  companyId: "fictional-company",
  origins: ["https://paper.example.invalid"],
  steps: [
    { action: "navigate", value: "https://paper.example.invalid/" },
    { action: "read", value: "main" },
  ],
  limits: { seconds: 1, requests: 10, bytes: 1024 },
  width: 800,
  height: 600,
  screenshots: false,
};
for (const start of [
  { type: "start", version: 2, request: scope },
  {
    type: "start",
    version: 1,
    request: {
      ...scope,
      steps: [{ action: "evaluate", value: "arbitraryCode()" }, scope.steps[1]],
    },
  },
]) {
  let created = false;
  await assert.rejects(
    serveBrowserWorker(
      Readable.from([Buffer.from(JSON.stringify(start) + "\n")]),
      new PassThrough(),
      async () => {
        created = true;
        throw new Error("Must not launch");
      },
    ),
  );
  assert.equal(created, false);
}

// A duplicate or foreign reply cannot satisfy a different pending browser request.
for (const badReply of [
  {
    type: "resource-result",
    jobId: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
    id: 1,
    reply: {},
  },
  { type: "resource-result", jobId: scope.jobId, id: 1, reply: {} },
]) {
  const input = new PassThrough(),
    output = new PassThrough();
  let closed = false;
  const listeners = new Map();
  const fake = {
    on(name, listener) {
      listeners.set(name, listener);
      return () => listeners.delete(name);
    },
    async send(method) {
      if (method === "Target.createBrowserContext")
        return { browserContextId: "context" };
      if (method === "Target.createTarget") return { targetId: "page" };
      if (method === "Target.attachToTarget") return { sessionId: "session" };
      if (method === "Page.navigate") {
        listeners.get("Fetch.requestPaused")?.(
          {
            requestId: "resource",
            request: { url: "https://paper.example.invalid/", method: "GET" },
          },
          "session",
        );
        return new Promise(() => {});
      }
      return {};
    },
  };
  const serving = serveBrowserWorker(input, output, async () => ({
    cdp: fake,
    close: async () => {
      closed = true;
      return true;
    },
  }));
  input.write(
    JSON.stringify({ type: "start", version: 1, request: scope }) + "\n",
  );
  const reading = (async () => {
    for await (const frame of readFrames(output, 10, 10000, 100000)) {
      const message = JSON.parse(frame.toString());
      if (message.type === "resource") {
        if (badReply.jobId === scope.jobId)
          input.write(
            JSON.stringify({
              type: "resource-result",
              jobId: scope.jobId,
              id: 1,
              reply: {
                status: 200,
                headers: { "Content-Type": "text/plain" },
                bodyBase64: "",
              },
            }) + "\n",
          );
        input.write(JSON.stringify(badReply) + "\n");
      }
      if (message.type === "result") return message;
    }
  })();
  const served = await serving;
  const message = await reading;
  assert.equal(served.result.state, "failed");
  assert.deepEqual(served.result.captures, []);
  assert.equal(message.browserExited, true);
  assert.equal(closed, true);
  output.destroy();
}
console.log(
  "Browser framing and adverse worker-channel checks passed. Torn receipts remain torn.",
);
