import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { createReadStream } from "node:fs";
import {
  lstat,
  readFile,
  readdir,
  realpath,
  writeFile,
} from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { execFileSync } from "node:child_process";

const workerFiles = [
  "driver.mjs",
  "protocol.mjs",
  "runner.mjs",
  "verify-image.mjs",
  "pin.json",
];

export function validateInventory(pin, inventory) {
  assert.equal(pin.schema, 1, "Unknown browser pin schema");
  assert.equal(pin.status, "qualification-candidate");
  assert.equal(pin.platform, "linux/amd64");
  assert.match(
    pin.baseImage,
    /^mcr\.microsoft\.com\/playwright@sha256:[a-f0-9]{64}$/,
  );
  assert.equal(inventory.schema, 1);
  assert.equal(inventory.baseImage, pin.baseImage);
  assert.equal(inventory.platform, "linux");
  assert.equal(inventory.architecture, "x64");
  assert.equal(
    Number(inventory.nodeVersion.match(/^v(\d+)\./)?.[1]),
    pin.node.major,
    "Unexpected Node major",
  );
  assert.match(inventory.nodeVersion, /^v\d+\.\d+\.\d+$/);
  assert.equal(
    inventory.chromiumVersion.trim().split(/\s+/).at(-1),
    pin.chromium.version,
    "Unexpected Chromium version",
  );
  assert.equal(inventory.nodePath, pin.node.executable);
  assert.equal(inventory.chromiumPath, pin.chromium.executable);
  assert.equal(
    inventory.qualified,
    false,
    "An image inventory cannot grant runtime qualification",
  );
  assert.match(inventory.nodeSha256, /^[a-f0-9]{64}$/);
  assert.match(inventory.chromiumSha256, /^[a-f0-9]{64}$/);
  for (const file of workerFiles)
    assert.match(inventory.files?.[file] ?? "", /^[a-f0-9]{64}$/);
}

export function requireProtectedFile(file, stat) {
  assert.equal(stat.uid, 0, `${file} is not root-owned`);
  assert.equal(stat.gid, 0, `${file} has an unexpected group`);
  assert.equal(stat.mode & 0o022, 0, `${file} is writable outside its owner`);
}

async function protectedPath(file) {
  // Check the real file and every directory leading to it. A locked door is little use in a movable wall.
  const actual = await realpath(file);
  for (let current = path.dirname(file); ; current = path.dirname(current)) {
    const stat = await lstat(current);
    assert.ok(
      !stat.isSymbolicLink(),
      `${current} must be an actual protected directory`,
    );
    requireProtectedFile(current, stat);
    if (current === path.dirname(current)) break;
  }
  for (let current = actual; ; current = path.dirname(current)) {
    requireProtectedFile(current, await lstat(current));
    if (current === path.dirname(current)) break;
  }
  return actual;
}

async function protectedTree(directory, allowedRoot = directory) {
  for (const item of await readdir(directory, { withFileTypes: true })) {
    const file = path.join(directory, item.name);
    const stat = await lstat(file);
    if (stat.isSymbolicLink()) {
      const resolved = await protectedPath(file);
      assert.ok(
        resolved.startsWith(allowedRoot + "/") ||
          resolved.startsWith("/usr/") ||
          resolved.startsWith("/lib/"),
        "Unexpected browser symlink target",
      );
    } else {
      requireProtectedFile(file, stat);
      assert.ok(
        item.isDirectory() || item.isFile(),
        "Unexpected special file in the browser bundle",
      );
      if (item.isDirectory()) await protectedTree(file, allowedRoot);
    }
  }
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
    "Image inventory runs only inside the Linux build",
  );
  assert.equal(process.arch, "x64");
  assert.equal(process.getuid(), 0);
  const root = path.dirname(fileURLToPath(import.meta.url));
  assert.equal(
    root,
    "/opt/hirezero/browser",
    "Use the copied image files, not a host checkout",
  );
  const pin = JSON.parse(await readFile(path.join(root, "pin.json"), "utf8"));
  const nodePath = await protectedPath("/usr/local/bin/node");
  const chromiumPath = await protectedPath("/usr/bin/chromium");
  await protectedTree(path.dirname(chromiumPath));
  for (const file of workerFiles) await protectedPath(path.join(root, file));
  // Version switches only; no page, browser profile, daemon or network is started by this inventory.
  const options = {
    encoding: "utf8",
    timeout: 5000,
    maxBuffer: 4096,
    env: { PATH: "/usr/bin:/bin", LANG: "C.UTF-8" },
  };
  const inventory = {
    schema: 1,
    qualified: false,
    platform: process.platform,
    architecture: process.arch,
    baseImage: pin.baseImage,
    nodePath,
    nodeVersion: execFileSync(nodePath, ["--version"], options).trim(),
    nodeSha256: await sha256(nodePath),
    chromiumPath,
    chromiumVersion: execFileSync(chromiumPath, ["--version"], options).trim(),
    chromiumSha256: await sha256(chromiumPath),
    files: Object.fromEntries(
      await Promise.all(
        workerFiles.map(async (file) => [
          file,
          await sha256(path.join(root, file)),
        ]),
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
    "PASS browser image inventory. The butler has counted the keys; runtime qualification still holds the door.",
  );
}

if (
  process.argv[1] &&
  path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  await inspectImage();
}
