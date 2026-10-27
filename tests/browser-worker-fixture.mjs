// Test-only process boundary: production must supply a qualified network-free sandbox and an unprivileged browser.
// This file has no fixture HTML or network client. Every page byte must arrive from the .NET broker under test.
import fs from "node:fs/promises";
import path from "node:path";
import os from "node:os";
import { spawn } from "node:child_process";
import { once } from "node:events";
import { setTimeout as delay } from "node:timers/promises";
import { pipeCdp, serveBrowserWorker } from "../runtime/browser/protocol.mjs";

const chrome = process.env.HIREZERO_TEST_CHROME;
if (!chrome || !path.isAbsolute(chrome))
  throw new Error("Select an installed test Chrome executable.");
const temporary = await fs.mkdtemp(
  path.join(os.tmpdir(), "hirezero-browser-protocol-"),
);
let browser, cdp, close;
try {
  await serveBrowserWorker(process.stdin, process.stdout, async (_, signal) => {
    signal.throwIfAborted();
    browser = spawn(
      chrome,
      [
        "--headless=new",
        "--disable-gpu",
        "--disable-background-networking",
        "--disable-sync",
        "--disable-default-apps",
        "--no-first-run",
        "--no-default-browser-check",
        "--disable-component-update",
        "--disable-domain-reliability",
        "--proxy-server=http://127.0.0.1:9",
        "--proxy-bypass-list=<-loopback>",
        "--host-resolver-rules=MAP * ~NOTFOUND",
        "--remote-debugging-pipe",
        "--user-data-dir=" + temporary,
        "about:blank",
      ],
      {
        windowsHide: true,
        stdio: ["ignore", "ignore", "ignore", "pipe", "pipe"],
      },
    );
    const exited = once(browser, "exit");
    exited.catch(() => {});
    cdp = pipeCdp(browser.stdio[4], browser.stdio[3]);
    let closing;
    close = () =>
      (closing ??= (async () => {
        if (browser.exitCode === null && browser.signalCode === null)
          await Promise.race([
            cdp.send("Browser.close").catch(() => {}),
            delay(750, undefined, { ref: false }),
          ]);
        await cdp.close();
        if (browser.exitCode === null && browser.signalCode === null)
          browser.kill();
        await Promise.race([
          exited.catch(() => {}),
          delay(2500, undefined, { ref: false }),
        ]);
        return browser.exitCode !== null || browser.signalCode !== null;
      })());
    return { cdp, close };
  });
} finally {
  if (close) await close();
  if (browser && browser.exitCode === null && browser.signalCode === null)
    throw new Error(
      "Owned test browser did not exit; its profile was retained.",
    );
  if (
    path.dirname(temporary) === path.resolve(os.tmpdir()) &&
    path.basename(temporary).startsWith("hirezero-browser-protocol-")
  )
    await fs.rm(temporary, {
      recursive: true,
      force: true,
      maxRetries: 5,
      retryDelay: 200,
    });
}
