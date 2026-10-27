import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { delay } from "./browser-smoke-wait.mjs";

// Real UI and local store; fictional minutes, no external work. The secretary keeps receipts.
export async function meetingMemoryJourney({
  send,
  evaluate,
  until,
  click,
  url,
  repo,
  meetingId,
}) {
  const waitText = (text) =>
    until(
      () =>
        evaluate(
          `document.querySelector('.co-memory')?.innerText.includes(${JSON.stringify(text)})`,
        ),
      text,
    );
  const field = async (label, value) => {
    await evaluate(
      `(()=>{const label=[...document.querySelectorAll('.co-memory label')].find(l=>[...l.childNodes].filter(n=>n.nodeType===3).map(n=>n.textContent).join('').trim()===${JSON.stringify(label)});if(!label)throw Error('Missing memory field');const el=label.querySelector('input,textarea,select');const proto=el.tagName==='SELECT'?HTMLSelectElement.prototype:el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event(el.tagName==='SELECT'?'change':'input',{bubbles:true}));})()`,
    );
    await delay(50);
  };
  const disabled = (label) =>
    evaluate(
      `[...document.querySelectorAll('button')].find(b=>b.textContent===${JSON.stringify(label)})?.disabled`,
    );
  const memories = () =>
    fetch(url + "/api/company/memory").then((r) => r.json());
  const decision =
    "Fictional decision: wait. Follow-up requires separate approval.";
  await click("Remember a decision");
  await waitText("What should the team remember?");
  assert.equal(
    await evaluate(`new URLSearchParams(location.search).get('memoryMeeting')`),
    meetingId,
  );
  await click("New memory");
  await field("Title", "Fictional unfinished preference");
  await field("Statement", "Preserve this separate draft.");
  await click("Open accepted minutes");
  await until(
    () =>
      evaluate(
        `document.getElementById(${JSON.stringify("co-meetings-" + meetingId)})===document.activeElement`,
      ),
    "exact minutes from handoff",
  );
  await click("Remember a decision");
  await waitText("Its unsaved text is preserved.");
  assert.equal(await disabled("Draft a decision from these minutes"), true);
  assert.equal(
    await evaluate(
      "document.querySelector('.co-memory-editor textarea').value",
    ),
    "Preserve this separate draft.",
  );
  await click("Discard unsaved edits");
  await click("Draft a decision from these minutes");
  await until(
    () => disabled("Save memory draft").then((value) => value === false),
    "cited minutes ready",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-memory-editor textarea').value",
    ),
    decision,
  );
  await field(
    "Statement",
    "Wait for owner approval before any campaign follow-up.",
  );
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-memory-editor textarea')?.value==='Wait for owner approval before any campaign follow-up.'",
      ),
    "edited decision survives reload",
  );
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-memory-editor label')].find(l=>[...l.childNodes].filter(n=>n.nodeType===3).map(n=>n.textContent).join('').trim()==='Exact supporting quote').querySelector('textarea').value",
    ),
    decision,
  );
  await until(
    () => disabled("Save memory draft").then((value) => value === false),
    "citation checked after reload",
  );
  // Source failure after a refresh must preserve text and block saving until a real retry succeeds.
  await evaluate(
    "window.meetingMemoryFetch=window.fetch;window.failMeetingSource=true;window.fetch=(input,init)=>window.failMeetingSource&&String(input).includes('/company/memory/meetings/')?Promise.reject(new TypeError('Fictional minutes outage')):window.meetingMemoryFetch(input,init)",
  );
  await field("Cite accepted minutes", "");
  await field("Cite accepted minutes", meetingId);
  await waitText("The information could not be loaded. Try again.");
  assert.equal(await disabled("Save memory draft"), true);
  assert.equal(
    await evaluate(
      "document.querySelector('.co-memory-editor textarea').value",
    ),
    "Wait for owner approval before any campaign follow-up.",
  );
  await evaluate("window.failMeetingSource=false");
  await click("Check cited minutes again");
  await until(
    () => disabled("Save memory draft").then((value) => value === false),
    "explicit citation retry recovers",
  );
  await field("Exact supporting quote", decision);
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
    await evaluate(
      "document.querySelector('.co-memory-editor').scrollIntoView({block:'start'})",
    );
    assert.equal(
      await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
      true,
    );
    const shot = await send("Page.captureScreenshot", { format: "png" });
    await fs.writeFile(
      path.join(repo, `test-results/meeting-memory-draft-${size}.png`),
      Buffer.from(shot.data, "base64"),
    );
  }
  await click("Save memory draft");
  await waitText("Memory draft saved.");
  const draft = (await memories()).find(
    (row) => row.memory.definition.meetingId === meetingId,
  );
  assert.equal(draft.memory.state, "draft");
  assert.equal(draft.usable, false);
  assert.equal(draft.memory.meetingOrigin.quote, decision);
  assert.equal(await disabled("Approve memory"), true);
  await field("Task or topic", "campaign follow-up");
  await click("Preview memory");
  await waitText("No matching current approved memory");
  await evaluate("document.querySelector('.co-memory-check input').click()");
  await click("Approve memory");
  await waitText("Memory approved.");
  await click("Preview memory");
  await until(
    () =>
      evaluate(
        "document.querySelectorAll('.co-memory-matches article').length===1",
      ),
    "approved decision retrieved",
  );
  assert.equal(
    await evaluate(
      `document.querySelector('.co-memory-matches').innerText.includes(${JSON.stringify(decision)})`,
    ),
    true,
  );
  const approved = (await memories()).find(
    (row) => row.memory.id === draft.memory.id,
  );
  assert.equal(approved.usable, true);
  assert.deepEqual(approved.memory.meetingOrigin, draft.memory.meetingOrigin);
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
    await evaluate(
      "document.querySelector('.co-memory-review').scrollIntoView({block:'start'})",
    );
    assert.equal(
      await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
      true,
    );
    const shot = await send("Page.captureScreenshot", { format: "png" });
    await fs.writeFile(
      path.join(repo, `test-results/meeting-memory-review-${size}.png`),
      Buffer.from(shot.data, "base64"),
    );
  }
  await click("Open cited minutes");
  await until(
    () =>
      evaluate(
        `document.getElementById(${JSON.stringify("co-meetings-" + meetingId)})===document.activeElement`,
      ),
    "citation returns to exact accepted minutes",
  );
  await fs.writeFile(
    path.join(repo, "test-results/meeting-memory-browser.json"),
    JSON.stringify(
      {
        exactMinutesSource: true,
        separateApproval: true,
        unfinishedDraftPreserved: true,
        editedDraftSurvivesReload: true,
        sourceFailurePreservesDraft: true,
        sourceRetry: true,
        approvedRetrieval: true,
        exactCitationNavigation: true,
        mobileOverflow: false,
        externalCalls: 0,
      },
      null,
      2,
    ),
  );
}
