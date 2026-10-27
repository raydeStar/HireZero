import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";

// Real UI/storage with synthetic hosted-session transport. HostedSignInTests separately exercises real
// cookie/OIDC middleware and stale-member refusal; this fixture never claims to authenticate a live account.
export async function memberSessionJourney({
  send,
  evaluate,
  click,
  until,
  idle,
  url,
  repo,
  secondaryCompany,
}) {
  await send("Page.addScriptToEvaluateOnNewDocument", {
    source: `(() => {
      window.__sessionPage = crypto.randomUUID();
      window.__memberRequests = [];
      const original = window.fetch.bind(window);
      let actualScope;
      window.fetch = async (input, options = {}) => {
        const pathname = new URL(typeof input === 'string' ? input : input.url, location.href).pathname;
        if (pathname === '/api/session') {
          if (sessionStorage.getItem('fictional-malformed-session') === '1') {
            sessionStorage.removeItem('fictional-malformed-session');
            return new Response('{}', {status:200,headers:{'Content-Type':'application/json'}});
          }
          const real = await original(input, options);
          const body = await real.json();
          actualScope = body.browserScope;
          const member = sessionStorage.getItem('fictional-member') || 'first';
          window.__sessionMember = member;
          const choiceMode = sessionStorage.getItem('fictional-company-choices');
          if (member !== 'none' && choiceMode &&
              (['empty','malformed'].includes(choiceMode) || new URLSearchParams(location.search).get('company') !== ${JSON.stringify(secondaryCompany)})) {
            const choices = choiceMode === 'empty' ? [] : choiceMode === 'malformed' ? [{id:'../escape',name:'Invalid',role:'owner'}] :
              [{id:${JSON.stringify(secondaryCompany)},name:'Fictional Harbor',role:'viewer'},
                ...(choiceMode === 'multiple' ? [{id:'local-company',name:'Fictional Very Long Company Name for Mobile Layout Review',role:'approver'}] : [])];
            return new Response(JSON.stringify({accessMode:'hosted',signedIn:true,login:member+'@example.invalid',
              browserScope:(member==='first'?'a':'b').repeat(64),companySelectionRequired:true,companies:choices}),
              {status:200,headers:{'Content-Type':'application/json'}});
          }
          return new Response(JSON.stringify({...body, accessMode:'hosted', signedIn:member!=='none',
            login:member==='none'?null:member+'@example.invalid',
            browserScope:member==='none'?null:(member==='first'?'a':'b').repeat(64)}),
            {status:200, headers:{'Content-Type':'application/json'}});
        }
        if (pathname === '/signout') {
          const attempts = Number(sessionStorage.getItem('fictional-signout-attempts') || '0') + 1;
          sessionStorage.setItem('fictional-signout-attempts', String(attempts));
          if (attempts === 1) {
            sessionStorage.setItem('fictional-malformed-session','1');
            return new Response('{}', {status:503});
          }
          sessionStorage.setItem('fictional-member', 'none');
          throw new TypeError('Fictional lost sign-out reply after cookie expiry');
        }
        const headers = new Headers(options.headers);
        window.__memberRequests.push({path:pathname, member:headers.get('X-HireZero-Member'), method:options.method || 'GET'});
        if (pathname === '/api/company/documents/retention' && sessionStorage.getItem('fictional-stale-request') === '1') {
          sessionStorage.removeItem('fictional-stale-request');
          sessionStorage.setItem('fictional-member', 'second');
          return new Response(JSON.stringify({error:'Fictional account changed', code:'session-changed'}),
            {status:409, headers:{'Content-Type':'application/json'}});
        }
        // Only this test transport maps its simulated accounts to the disposable local fixture owner.
        if (headers.has('X-HireZero-Member')) headers.set('X-HireZero-Member', actualScope);
        return original(input, {...options, headers});
      };
    })();`,
  });
  const draftKey = "hirezero-documents-v1:local-companydraft";
  const scoped = (member) =>
    `hirezero-member-v1:${(member === "first" ? "a" : "b").repeat(64)}:${draftKey}`;
  await evaluate(
    `sessionStorage.setItem(${JSON.stringify(draftKey)}, JSON.stringify({title:'Unattributed legacy title',text:'Unattributed legacy text',audience:'company'}))`,
  );
  await send("Page.navigate", {
    url: url + "/?company=local-company&view=departments&section=documents",
  });
  const loaded = async (member) => {
    await until(
      () =>
        evaluate(
          `window.__sessionMember===${JSON.stringify(member)} && !!document.querySelector('.co-documents') && !document.querySelector('.co-documents').closest('[hidden]')`,
        ),
      member + " documents",
    );
    await idle();
  };
  await loaded("first");
  assert.equal(
    await evaluate("!!document.querySelector('.co-document-editor')"),
    false,
    "Unattributed legacy hosted drafts are not displayed",
  );
  const type = async (selector, value) => {
    await evaluate(
      `(() => { const input=document.querySelector(${JSON.stringify(selector)}); const prototype=input.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype; Object.getOwnPropertyDescriptor(prototype,'value').set.call(input,${JSON.stringify(value)}); input.dispatchEvent(new Event('input',{bubbles:true})); })()`,
    );
  };
  await click("Add a document");
  await type(
    ".co-document-editor input:not([type])",
    "First member unfinished title",
  );
  await type(
    ".co-document-editor textarea",
    "Fictional first member private draft",
  );
  assert.equal(
    await evaluate(
      `JSON.parse(sessionStorage.getItem(${JSON.stringify(scoped("first"))})).text`,
    ),
    "Fictional first member private draft",
  );
  let page = await evaluate("window.__sessionPage");
  await evaluate("window.dispatchEvent(new Event('focus'))");
  await loaded("first");
  assert.equal(
    await evaluate("window.__sessionPage"),
    page,
    "Same-member focus preserves the mounted editor",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-document-editor textarea').value",
    ),
    "Fictional first member private draft",
  );
  const switchMember = async (member) => {
    page = await evaluate("window.__sessionPage");
    await evaluate(
      `sessionStorage.setItem('fictional-member',${JSON.stringify(member)}); window.dispatchEvent(new Event('focus'))`,
    );
    await until(
      () => evaluate(`window.__sessionPage!==${JSON.stringify(page)}`),
      "fresh document for changed member",
    );
    await loaded(member);
  };
  await switchMember("second");
  assert.equal(
    await evaluate("!!document.querySelector('.co-document-editor')"),
    false,
    "The second member cannot see the first member's unsaved draft",
  );
  await click("Add a document");
  await type(
    ".co-document-editor input:not([type])",
    "Second member unfinished title",
  );
  await type(
    ".co-document-editor textarea",
    "Fictional second member private draft",
  );
  await switchMember("first");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-document-editor textarea').value",
    ),
    "Fictional first member private draft",
    "Returning to the first member restores only their draft",
  );
  assert.ok(
    (await evaluate("window.__memberRequests"))
      .filter((r) => r.path.startsWith("/api/"))
      .every((r) => r.member === "a".repeat(64)),
    "Loaded requests retain their member marker",
  );
  for (const [size, width, height] of [
    ["desktop", 1440, 1000],
    ["mobile", 390, 844],
  ]) {
    await send("Emulation.setDeviceMetricsOverride", {
      width,
      height,
      deviceScaleFactor: 1,
      mobile: size === "mobile",
    });
    assert.equal(
      await evaluate("document.documentElement.scrollWidth<=innerWidth"),
      true,
    );
    assert.ok(
      await evaluate(
        "(()=>{const r=document.querySelector('[aria-label=\"Sign out of HireZero\"]').getBoundingClientRect();return r.width>0&&r.left>=0&&r.right<=innerWidth;})()",
      ),
    );
    if (size === "mobile")
      assert.ok(
        await evaluate(
          "(()=>{const button=document.querySelector('.co-signout').getBoundingClientRect();const title=document.querySelector('.fe-topbar h1').getBoundingClientRect();return Math.abs(button.top-title.top)<20;})()",
        ),
        "Sign-out stays in the mobile header row",
      );
    const shot = await send("Page.captureScreenshot", {
      format: "png",
      captureBeyondViewport: false,
    });
    await fs.writeFile(
      path.join(repo, `test-results/member-session-${size}.png`),
      Buffer.from(shot.data, "base64"),
    );
  }
  // A stale in-flight request also triggers reauthentication without relying on a focus event.
  page = await evaluate("window.__sessionPage");
  await evaluate("sessionStorage.setItem('fictional-stale-request','1')");
  await click("Refresh retention setting");
  await until(
    () => evaluate(`window.__sessionPage!==${JSON.stringify(page)}`),
    "stale request rechecks changed account",
  );
  await loaded("second");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-document-editor textarea').value",
    ),
    "Fictional second member private draft",
  );
  await evaluate(
    "document.querySelector('[aria-label=\"Sign out of HireZero\"]').click()",
  );
  await until(
    () => evaluate("document.body.innerText.includes('Retry sign out')"),
    "unconfirmed sign-out keeps a retry screen",
  );
  assert.equal(
    await evaluate("!!document.querySelector('.co-workspace')"),
    false,
    "An uncertain sign-out never restores private content",
  );
  await click("Retry sign out");
  await until(
    () => evaluate("!!document.querySelector('a[href=\"/signin\"]')"),
    "lost sign-out reply reconciles through session read",
  );
  assert.equal(
    await evaluate("sessionStorage.getItem('fictional-signout-attempts')"),
    "2",
  );
  assert.equal(
    await evaluate("!!document.querySelector('.co-workspace')"),
    false,
  );
  assert.equal(
    await evaluate(
      `JSON.parse(sessionStorage.getItem(${JSON.stringify(scoped("second"))})).text`,
    ),
    "Fictional second member private draft",
    "Sign-out does not erase account-scoped recovery state",
  );
  await evaluate("sessionStorage.setItem('fictional-member','first')");
  await send("Page.navigate", {
    url: url + "/?view=departments&section=documents",
  });
  await loaded("first");
  await evaluate(
    "sessionStorage.setItem('fictional-member','none');window.dispatchEvent(new StorageEvent('storage',{key:'hirezero-session-change',newValue:'fictional-signout'}))",
  );
  await until(
    () => evaluate("!!document.querySelector('a[href=\"/signin\"]')"),
    "another tab's sign-out hides the workspace",
  );
  assert.equal(
    await evaluate("!!document.querySelector('.co-workspace')"),
    false,
  );
  const chooser = async (mode) => {
    await evaluate(
      `sessionStorage.setItem('fictional-member','first');sessionStorage.setItem('fictional-company-choices',${JSON.stringify(mode)})`,
    );
    await send("Page.navigate", {
      url: url + "/?view=task&task=fictional-old-company-task",
    });
    await until(
      () => evaluate("!!document.querySelector('.co-company-choice')"),
      mode + " company selection",
    );
    await idle();
    assert.equal(
      await evaluate("!!document.querySelector('.co-workspace')"),
      false,
    );
    assert.equal(
      await evaluate("window.__memberRequests.length"),
      0,
      "Selection never mounts workspace effects or reads another company",
    );
  };
  await chooser("single");
  assert.equal(
    await evaluate("document.querySelectorAll('.co-company-choice li').length"),
    1,
  );
  await evaluate(
    "document.querySelector('.co-company-choice li button').click()",
  );
  await until(
    () =>
      evaluate(
        `location.search===${JSON.stringify("?company=" + secondaryCompany)} && !!document.querySelector('.co-workspace')`,
      ),
    "explicit company choice opens its fresh home",
  );
  await idle();
  assert.equal(
    await evaluate("new URLSearchParams(location.search).has('task')"),
    false,
  );
  assert.equal(
    await evaluate("!!document.querySelector('.co-documents')"),
    false,
    "Previous task and document routes are discarded",
  );
  // A real role recheck takes this same path after the server removes the membership.
  page = await evaluate("window.__sessionPage");
  await evaluate(
    "sessionStorage.setItem('fictional-company-choices','empty');window.dispatchEvent(new Event('focus'))",
  );
  await until(
    () =>
      evaluate(
        `window.__sessionPage!==${JSON.stringify(page)} && document.body.innerText.includes('No company access')`,
      ),
    "revocation removes mounted workspace state",
  );
  assert.equal(
    await evaluate("!!document.querySelector('.co-workspace')"),
    false,
  );
  assert.equal(
    await evaluate("document.querySelectorAll('.co-company-choice li').length"),
    0,
  );
  await evaluate(
    "sessionStorage.setItem('fictional-company-choices','multiple')",
  );
  await click("Refresh access");
  await until(
    () => evaluate("!!document.querySelector('.co-workspace')"),
    "refresh recovers restored membership in selected company",
  );
  await chooser("multiple");
  assert.equal(
    await evaluate("document.querySelectorAll('.co-company-choice li').length"),
    2,
  );
  for (const [size, width, height] of [
    ["desktop", 1440, 1000],
    ["mobile", 390, 844],
  ]) {
    await send("Emulation.setDeviceMetricsOverride", {
      width,
      height,
      deviceScaleFactor: 1,
      mobile: size === "mobile",
    });
    assert.equal(
      await evaluate("document.documentElement.scrollWidth<=innerWidth"),
      true,
    );
    assert.equal(
      await evaluate(
        "[...document.querySelectorAll('.co-company-choice button')].every(b=>b.getBoundingClientRect().height>=44)",
      ),
      true,
    );
    assert.equal(
      await evaluate("document.documentElement.scrollHeight<=innerHeight"),
      true,
      "The entry screen does not add an empty header strip or unnecessary vertical scrolling",
    );
    const shot = await send("Page.captureScreenshot", {
      format: "png",
      captureBeyondViewport: false,
    });
    await fs.writeFile(
      path.join(repo, `test-results/company-entry-${size}.png`),
      Buffer.from(shot.data, "base64"),
    );
  }
  await chooser("empty");
  await click("Sign out");
  await until(
    () => evaluate("!!document.querySelector('a[href=\"/signin\"]')"),
    "revoked member can sign out",
  );
  await evaluate(
    "sessionStorage.setItem('fictional-member','first');sessionStorage.setItem('fictional-company-choices','malformed')",
  );
  await send("Page.navigate", { url: url + "/" });
  await until(
    () => evaluate("document.body.innerText.includes('Retry sign-in check')"),
    "malformed company list is refused",
  );
  assert.equal(
    await evaluate("!!document.querySelector('.co-workspace')"),
    false,
  );
  assert.equal(
    await evaluate("!!document.querySelector('.co-company-choice')"),
    false,
  );
  assert.equal(await evaluate("window.__memberRequests.length"), 0);
  return {
    hostedUiTransport: "synthetic; real OIDC middleware tested separately",
    memberDraftIsolation: true,
    sameMemberDraftRecovery: true,
    staleRequestRechecksSession: true,
    uncertainSignOutReconciled: true,
    otherTabSignOutObserved: true,
    secondaryCompanySelection: true,
    revokedMembershipRecovery: true,
    invalidCompanyChoiceRefused: true,
  };
}
