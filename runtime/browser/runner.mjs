// Immutable PID 1 entrypoint for the qualified browser image. Never use this to browse on the host.
import { spawn } from "node:child_process";
import { once } from "node:events";
import { setTimeout as delay } from "node:timers/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { pipeCdp, serveBrowserWorker } from "./protocol.mjs";

export function requireSupervisor() {
  if (
    process.platform !== "linux" ||
    process.getuid?.() !== 0 ||
    process.pid !== 1
  )
    throw new Error("A qualified Linux container supervisor is required.");
}

async function run() {
  requireSupervisor();
  process.setgroups([]);
  // This independent PID 1 watchdog also bounds a blocked final stdout write or a stalled cleanup callback.
  let watchdog = setTimeout(() => process.exit(124), 5000);
  const receipt = await serveBrowserWorker(
    process.stdin,
    process.stdout,
    async (request, signal) => {
      signal.throwIfAborted();
      clearTimeout(watchdog);
      watchdog = setTimeout(
        () => process.exit(124),
        (request.limits.seconds + 12) * 1000,
      );
      const child = spawn(
        "/usr/bin/chromium",
        [
          "--headless=new",
          "--disable-gpu",
          "--disable-background-networking",
          "--disable-sync",
          "--disable-default-apps",
          "--disable-component-update",
          "--disable-domain-reliability",
          "--disable-extensions",
          "--disable-component-extensions-with-background-pages",
          "--disable-breakpad",
          "--no-first-run",
          "--no-default-browser-check",
          "--disable-dev-shm-usage",
          "--remote-debugging-pipe",
          "--user-data-dir=/work/browser-profile",
          // The itinerary creates its own page. Even an unused spare tab sends a bill.
          "--no-startup-window",
        ],
        {
          cwd: "/work",
          uid: 65534,
          gid: 65534,
          detached: true,
          env: {
            PATH: "/usr/local/bin:/usr/bin:/bin",
            HOME: "/work",
            TMPDIR: "/tmp",
            XDG_CACHE_HOME: "/work/cache",
            XDG_CONFIG_HOME: "/work/config",
            LANG: "C.UTF-8",
          },
          stdio: ["ignore", "ignore", "ignore", "pipe", "pipe"],
        },
      );
      const exited = once(child, "exit");
      exited.catch(() => {});
      const cdp = pipeCdp(child.stdio[4], child.stdio[3]);
      let closing;
      return {
        cdp,
        close: () =>
          (closing ??= (async () => {
            if (child.exitCode === null && child.signalCode === null)
              await Promise.race([
                cdp.send("Browser.close").catch(() => {}),
                delay(750, undefined, { ref: false }),
              ]);
            await cdp.close();
            if (
              child.exitCode === null &&
              child.signalCode === null &&
              child.pid
            ) {
              // Only this supervisor's freshly spawned process group; no PID comes from a page or host message.
              try {
                process.kill(-child.pid, "SIGKILL");
              } catch {}
            }
            await Promise.race([
              exited.catch(() => {}),
              delay(2500, undefined, { ref: false }),
            ]);
            return child.exitCode !== null || child.signalCode !== null;
          })()),
      };
    },
  );
  // PID 1 exiting also terminates residual descendants. Container release is confirmed separately by the host.
  process.exit(receipt ? 0 : 1);
}

if (
  process.argv[1] &&
  path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  try {
    await run();
  } catch {
    process.exit(2);
  } // No untrusted diagnostics are mixed into the bounded result stream.
}
