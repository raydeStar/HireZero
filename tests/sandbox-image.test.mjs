import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import {
  validateInventory,
  requireProtectedFile,
} from "../runtime/sandbox/verify-image.mjs";

const root = new URL("../runtime/sandbox/", import.meta.url);
const pin = JSON.parse(fs.readFileSync(new URL("pin.json", root), "utf8"));
const inventory = () => ({
  schema: 1,
  qualified: false,
  platform: "linux",
  architecture: "x64",
  baseImage: pin.baseImage,
  nodeVersion: "v" + pin.node.version,
  nodePath: pin.node.executable,
  worker: pin.worker,
  nodeSha256: "a".repeat(64),
  files: Object.fromEntries(
    ["runner.mjs", "verify-image.mjs", "pin.json"].map((name) => [
      name,
      "b".repeat(64),
    ]),
  ),
});

test("script inventory refuses changed versions, paths, platforms, missing hashes and qualification claims", () => {
  validateInventory(pin, inventory());
  for (const changed of [
    { nodeVersion: "v22.0.0" },
    { nodePath: "/tmp/node" },
    { architecture: "arm64" },
    { platform: "win32" },
    { baseImage: "node:22-alpine" },
    { qualified: true },
    { qualified: undefined },
    { nodeSha256: "unverified" },
    { files: {} },
    { worker: { protocol: 1, v8PoolSize: 4, uvThreadPoolSize: 4 } },
    { schema: 2 },
  ])
    assert.throws(() => validateInventory(pin, { ...inventory(), ...changed }));
});

test("the image recipe and context retain the reviewed immutable base and protected worker", () => {
  const recipe = fs.readFileSync(new URL("Dockerfile", root), "utf8");
  assert.ok(recipe.includes("FROM --platform=linux/amd64 " + pin.baseImage));
  assert.match(
    recipe,
    /COPY --chown=0:0 --chmod=0555 runner\.mjs verify-image\.mjs/,
  );
  assert.match(
    recipe,
    /RUN --network=none \/usr\/local\/bin\/node \/opt\/hirezero\/verify-image\.mjs/,
  );
  assert.doesNotMatch(recipe, /\b(?:apk|apt-get|npm|curl|wget)\b/);
  assert.deepEqual(
    fs
      .readFileSync(new URL(".dockerignore", root), "utf8")
      .trim()
      .split(/\r?\n/),
    [
      "**",
      "!Dockerfile",
      "!.dockerignore",
      "!runner.mjs",
      "!verify-image.mjs",
      "!pin.json",
    ],
  );
  const protectedFile = { uid: 0, gid: 0, mode: 0o555 };
  requireProtectedFile("fictional-worker", protectedFile);
  for (const changed of [
    { uid: 65534 },
    { gid: 65534 },
    { mode: 0o575 },
    { mode: 0o557 },
  ])
    assert.throws(() =>
      requireProtectedFile("fictional-worker", {
        ...protectedFile,
        ...changed,
      }),
    );
});

test("the image inventory refuses execution from a host checkout", () => {
  const result = spawnSync(
    process.execPath,
    [fileURLToPath(new URL("verify-image.mjs", root))],
    { encoding: "utf8", timeout: 5000, windowsHide: true },
  );
  assert.equal(result.status, 1);
  assert.equal(fs.existsSync(new URL("image-inventory.json", root)), false);
});
