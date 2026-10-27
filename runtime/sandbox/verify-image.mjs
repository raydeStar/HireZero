import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { createReadStream } from "node:fs";
import { lstat, readFile, realpath, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const files = ["runner.mjs", "verify-image.mjs", "pin.json"];

export function validateInventory(pin, inventory) {
  assert.equal(pin.schema, 1);
  assert.equal(pin.status, "qualification-candidate");
  assert.equal(pin.platform, "linux/amd64");
  assert.match(pin.baseImage, /^node:22-alpine@sha256:[a-f0-9]{64}$/);
  for (const digest of [pin.platformManifestDigest, pin.configDigest])
    assert.match(digest, /^sha256:[a-f0-9]{64}$/);
  assert.match(pin.node.version, /^22\.\d+\.\d+$/);
  assert.equal(pin.node.executable, "/usr/local/bin/node");
  assert.deepEqual(pin.worker, { protocol: 2, v8PoolSize: 1, uvThreadPoolSize: 1 });
  assert.equal(inventory.schema, 1);
  assert.equal(inventory.platform, "linux");
  assert.equal(inventory.architecture, "x64");
  assert.equal(inventory.baseImage, pin.baseImage);
  assert.equal(inventory.nodeVersion, "v" + pin.node.version);
  assert.equal(inventory.nodePath, pin.node.executable);
  assert.deepEqual(inventory.worker, pin.worker);
  assert.match(inventory.nodeSha256, /^[a-f0-9]{64}$/);
  assert.equal(
    inventory.qualified,
    false,
    "An inventory cannot qualify a runtime",
  );
  for (const file of files)
    assert.match(inventory.files?.[file] ?? "", /^[a-f0-9]{64}$/);
}

export function requireProtectedFile(file, stat) {
  assert.equal(stat.uid, 0, `${file} must be root-owned`);
  assert.equal(stat.gid, 0, `${file} must have the root group`);
  assert.equal(
    stat.mode & 0o022,
    0,
    `${file} must not be writable by other identities`,
  );
}

async function protectedPath(file) {
  const actual = await realpath(file);
  assert.ok(
    (await lstat(actual)).isFile(),
    "Expected an ordinary executable or worker file",
  );
  // Inspect both routes: a locked file is no defence when someone can replace the corridor.
  for (const start of [path.dirname(file), actual]) {
    for (let current = start; ; current = path.dirname(current)) {
      const stat = await lstat(current);
      assert.ok(!stat.isSymbolicLink(), `Unexpected link at ${current}`);
      requireProtectedFile(current, stat);
      if (current === path.dirname(current)) break;
    }
  }
  return actual;
}

async function sha256(file) {
  const hash = createHash("sha256");
  for await (const bytes of createReadStream(file)) hash.update(bytes);
  return hash.digest("hex");
}

async function inspectImage() {
  assert.equal(
    process.platform,
    "linux",
    "Inventory runs inside the Linux image build",
  );
  assert.equal(process.arch, "x64");
  assert.equal(process.getuid(), 0);
  const root = path.dirname(fileURLToPath(import.meta.url));
  assert.equal(
    root,
    "/opt/hirezero",
    "Use the image copy, never a host checkout",
  );
  const pin = JSON.parse(await readFile(path.join(root, "pin.json"), "utf8"));
  const nodePath = await protectedPath("/usr/local/bin/node");
  assert.equal(await realpath(process.execPath), nodePath);
  for (const file of files) await protectedPath(path.join(root, file));
  const inventory = {
    schema: 1,
    qualified: false,
    platform: process.platform,
    architecture: process.arch,
    baseImage: pin.baseImage,
    nodeVersion: process.version,
    nodePath,
    worker: pin.worker,
    nodeSha256: await sha256(nodePath),
    files: Object.fromEntries(
      await Promise.all(
        files.map(async (file) => [file, await sha256(path.join(root, file))]),
      ),
    ),
  };
  validateInventory(pin, inventory);
  await writeFile(
    path.join(root, "image-inventory.json"),
    JSON.stringify(inventory, null, 2) + "\n",
    { flag: "wx", mode: 0o444 },
  );
  console.log(
    "PASS script image inventory. The butler has checked the tools; qualification still holds the key.",
  );
}

if (
  process.argv[1] &&
  path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)
)
  await inspectImage();
