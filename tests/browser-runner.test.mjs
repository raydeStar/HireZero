import assert from "node:assert/strict";
import path from "node:path";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import { requireSupervisor } from "../runtime/browser/runner.mjs";
import {
  validateInventory,
  requireProtectedFile,
} from "../runtime/browser/verify-image.mjs";

// Routine checks must never launch the contained entrypoint on a developer's host or ordinary child process.
if (
  process.platform !== "linux" ||
  process.getuid?.() !== 0 ||
  process.pid !== 1
) {
  assert.throws(requireSupervisor, /qualified Linux container supervisor/);
  const refusal = spawnSync(
    process.execPath,
    [path.resolve(import.meta.dirname, "../runtime/browser/runner.mjs")],
    {
      input: '{"type":"start"}\n',
      encoding: "utf8",
      timeout: 5000,
      windowsHide: true,
    },
  );
  assert.equal(refusal.status, 2);
  assert.equal(refusal.stdout, "");
  assert.equal(refusal.stderr, "");
}
console.log(
  "Browser entrypoint refused host execution. The butler leaves this key in the qualified container.",
);

const browserRoot = path.resolve(import.meta.dirname, "../runtime/browser");
const pin = JSON.parse(
  fs.readFileSync(path.join(browserRoot, "pin.json"), "utf8"),
);
const recipe = fs.readFileSync(path.join(browserRoot, "Dockerfile"), "utf8");
assert.ok(
  recipe
    .split(/\r?\n/)
    .includes("FROM --platform=linux/amd64 " + pin.baseImage),
  "The recipe uses the locked platform-specific digest",
);
assert.ok(recipe.includes(pin.chromium.executable));
assert.ok(
  recipe.includes(
    'io.hirezero.browser.chromium="' + pin.chromium.version + '"',
  ),
);
assert.doesNotMatch(
  recipe,
  /(?:apt-get|apk|npm|npx|curl|wget)\s/,
  "No floating build-time package or browser fetch",
);
assert.doesNotMatch(
  recipe,
  /(?:COPY|ADD)\s+\.\s/,
  "The image never copies an entire checkout",
);
assert.doesNotMatch(
  recipe,
  /^\s*VOLUME\b/m,
  "No writable image volume bypasses the adapter envelope",
);
const ignore = fs
  .readFileSync(path.join(browserRoot, ".dockerignore"), "utf8")
  .trim()
  .split(/\r?\n/);
assert.deepEqual(ignore, [
  "**",
  "!Dockerfile",
  "!.dockerignore",
  "!pin.json",
  "!driver.mjs",
  "!protocol.mjs",
  "!runner.mjs",
  "!verify-image.mjs",
]);
const inventory = {
  schema: 1,
  qualified: false,
  platform: "linux",
  architecture: "x64",
  baseImage: pin.baseImage,
  nodeVersion: "v24.0.0",
  nodePath: pin.node.executable,
  nodeSha256: "a".repeat(64),
  chromiumVersion: "Google Chrome for Testing " + pin.chromium.version,
  chromiumPath: pin.chromium.executable,
  chromiumSha256: "b".repeat(64),
  files: Object.fromEntries(
    [
      "driver.mjs",
      "protocol.mjs",
      "runner.mjs",
      "verify-image.mjs",
      "pin.json",
    ].map((file) => [file, "c".repeat(64)]),
  ),
};
validateInventory(pin, inventory);
for (const mutation of [
  { qualified: true },
  { platform: "win32" },
  { architecture: "arm64" },
  { baseImage: pin.baseImage.replace(/.$/, "0") },
  { nodeVersion: "v22.0.0" },
  { chromiumVersion: "Google Chrome for Testing 1.2.3.4" },
  { chromiumPath: "/tmp/chrome" },
  { nodeSha256: "unknown" },
  { files: {} },
])
  assert.throws(() => validateInventory(pin, { ...inventory, ...mutation }));
requireProtectedFile("fictional-worker", { uid: 0, gid: 0, mode: 0o555 });
for (const stat of [
  { uid: 1, gid: 0, mode: 0o555 },
  { uid: 0, gid: 1, mode: 0o555 },
  { uid: 0, gid: 0, mode: 0o777 },
])
  assert.throws(() => requireProtectedFile("fictional-worker", stat));
const refusedInventory = spawnSync(
  process.execPath,
  [path.join(browserRoot, "verify-image.mjs")],
  {
    encoding: "utf8",
    timeout: 5000,
    windowsHide: true,
  },
);
assert.notEqual(
  refusedInventory.status,
  0,
  "An ordinary host checkout cannot emit an image inventory",
);
assert.equal(
  fs.existsSync(path.join(browserRoot, "image-inventory.json")),
  false,
);
console.log(
  "PASS browser image contract: pinned inputs, protected files and no counterfeit qualification; the inventory stays honest.",
);
