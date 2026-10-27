import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "../web/node_modules/typescript/lib/typescript.js";

const code = ts.transpileModule(
  fs.readFileSync(
    new URL("../web/src/company/companyCreation.ts", import.meta.url),
    "utf8",
  ),
  {
    compilerOptions: {
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2022,
    },
  },
).outputText;
class ApiError extends Error {
  constructor(status, code) {
    super("Fictional failure");
    this.status = status;
    this.code = code;
  }
}
function fixture() {
  const storage = new Map(),
    requests = [];
  let scenario = "ok",
    writeDenied = false,
    removeDenied = false,
    sequence = 0;
  function page(companyId = "") {
    const exports = {};
    const sessionStorage = {
      getItem: (key) => storage.get(key) ?? null,
      setItem: (key, value) => {
        if (writeDenied) throw new Error("Storage unavailable");
        storage.set(key, value);
      },
      removeItem: (key) => {
        if (removeDenied) throw new Error("Removal unavailable");
        storage.delete(key);
      },
    };
    vm.runInNewContext(code, {
      exports,
      crypto: {
        randomUUID: () =>
          `00000000-0000-4000-8000-${String(++sequence).padStart(12, "0")}`,
      },
      sessionStorage,
      require: () => ({
        memberStorage: sessionStorage,
        ApiError,
        company: () => companyId,
        api: async (path, method = "GET", body) => {
          requests.push({ path, method, body });
          if (method === "POST") {
            if (scenario === "refused") throw new ApiError(422);
            if (
              scenario === "lost" ||
              scenario === "outage" ||
              scenario === "not-sent" ||
              scenario === "old-server"
            )
              throw new ApiError(0);
            if (scenario === "server-error") throw new ApiError(503);
          } else if (scenario === "outage") throw new ApiError(503);
          else if (scenario === "not-sent")
            throw new ApiError(404, "company-creation-not-recorded");
          else if (scenario === "old-server") throw new ApiError(404);
          return {
            id: "fictional-created",
            name: "Fictional Studio",
            role: "owner",
          };
        },
      }),
    });
    return exports;
  }
  return {
    page,
    requests,
    scenario: (value) => {
      scenario = value;
    },
    denyWrite: () => {
      writeDenied = true;
    },
    denyRemoval: () => {
      removeDenied = true;
    },
  };
}

test("lost replies and server failures reconcile with a read, never another creation", async () => {
  for (const failure of ["lost", "server-error"]) {
    const f = fixture();
    f.scenario(failure);
    const page = f.page();
    assert.equal(
      (await page.createCompany("Fictional Studio")).id,
      "fictional-created",
    );
    assert.deepEqual(
      f.requests.map((r) => r.method),
      ["POST", "GET"],
    );
    assert.equal(page.pendingCompanyCreation(), null);
  }
});

test("an unresolved creation survives reload, blocks another POST and stays scoped to its source company", async () => {
  const f = fixture();
  f.scenario("outage");
  await assert.rejects(
    f.page("fictional-source").createCompany("Fictional Studio"),
    /unconfirmed/,
  );
  const reloaded = f.page("fictional-source");
  assert.ok(reloaded.pendingCompanyCreation());
  assert.equal(f.page("fictional-neighbor").pendingCompanyCreation(), null);
  await assert.rejects(
    reloaded.createCompany("Another Studio"),
    /Check the saved company/,
  );
  await assert.rejects(reloaded.checkCompanyCreation(), /Fictional failure/);
  f.scenario("ok");
  assert.equal((await reloaded.checkCompanyCreation()).id, "fictional-created");
  assert.equal(f.requests.filter((r) => r.method === "POST").length, 1);
  assert.equal(reloaded.pendingCompanyCreation(), null);
});

test("a refused request clears its pending receipt and unavailable storage prevents dispatch", async () => {
  const f = fixture();
  f.scenario("refused");
  const page = f.page();
  await assert.rejects(
    page.createCompany("Fictional Studio"),
    /Fictional failure/,
  );
  assert.equal(page.pendingCompanyCreation(), null);
  assert.equal(f.requests.length, 1);
  f.denyWrite();
  await assert.rejects(
    page.createCompany("Fictional Studio"),
    /Nothing was submitted/,
  );
  assert.equal(f.requests.length, 1);
});

test("failure to remove a confirmed receipt allows only read recovery after reload", async () => {
  const f = fixture();
  f.denyRemoval();
  const page = f.page();
  await page.createCompany("Fictional Studio");
  assert.equal(page.pendingCompanyCreation(), null);
  const reloaded = f.page();
  assert.ok(reloaded.pendingCompanyCreation());
  await assert.rejects(
    reloaded.createCompany("Another Studio"),
    /Check the saved company/,
  );
  await reloaded.checkCompanyCreation();
  assert.deepEqual(
    f.requests.map((r) => r.method),
    ["POST", "GET"],
  );
});

test("an explicit finish after a missing receipt reuses the original name and UUID across reload", async () => {
  const f = fixture();
  f.scenario("not-sent");
  await assert.rejects(
    f.page().createCompany("Original Fictional Studio"),
    (error) => error.canFinish === true,
  );
  const reloaded = f.page();
  assert.equal(
    reloaded.pendingCompanyCreation().name,
    "Original Fictional Studio",
  );
  await assert.rejects(
    reloaded.createCompany("Different Studio"),
    /Check the saved company/,
  );
  assert.equal(f.requests.filter((r) => r.method === "POST").length, 1);
  f.scenario("ok");
  await reloaded.finishCompanyCreation();
  const posts = f.requests.filter((r) => r.method === "POST");
  assert.equal(posts.length, 2);
  assert.equal(posts[0].body.requestId, posts[1].body.requestId);
  assert.equal(posts[0].body.name, posts[1].body.name);
  assert.equal(reloaded.pendingCompanyCreation(), null);
});

test("an unknown or older server's ordinary 404 does not offer a repeat", async () => {
  const f = fixture();
  f.scenario("old-server");
  await assert.rejects(
    f.page().createCompany("Fictional Studio"),
    (error) => error.canFinish === false,
  );
  assert.equal(f.requests.filter((r) => r.method === "POST").length, 1);
});
