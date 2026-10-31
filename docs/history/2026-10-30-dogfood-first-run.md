# Dogfooding a first run as a non-technical owner

The owner asked for HireZero to be dogfooded with hirezero.app as the business, from the point of view of someone with
little AI knowledge who just needs help getting through and managing things. The aim was to remove friction before the
owner's own walkthrough.

A fresh local company ran the current build on the pinned Luna connection, on port 5081 with its own data folder. Each
run below was one real Luna request; no retries were needed:
- the first draft, run twice, once before and once after the fixes;
- one change request through "Refine draft";
- one chat reply.

## What got in the way, and what changed

**First visit**
- Home opened with a greeting and a form, with no word on what HireZero does. A first visit now shows three steps: tell
  us what you need, get a private draft, you decide. The third says nothing is posted or sent for you.
- The sidebar said "Hire and onboard an employee", although the goal form sets the employee up. It now says the team
  shows up after the first request.
- The cockpit showed three empty counts. An empty company now gets one sentence about what the panel is for.

**The goal form**
- "Uses your details; missing facts stay unknown" and "unverified references" became plain sentences about what is
  used and what will be marked for checking.
- The run terms opened with "1 intended existing credit(s) · 90s local deadline · 6,000 output-token target". The form
  now says what happens in a sentence: who writes the draft, how long it usually takes, and that nothing is posted, sent,
  bought or retried. The exact terms remain under "Run details", and the consent names the one private draft.
- While the draft was being written, the page showed one line and a disabled "Working…" button. It now lists the steps
  with the current one marked, and asks the owner to keep the page open.
- The setup card described "the asset your goal calls for". It now promises a ready-to-use draft and short notes on
  anything to check.

**The draft**
- The work was titled "Marketing goal draft" (a skill name) and "Request 1". It is now "Marketing draft".
  - A request number appears only when a conversation has more than one request.
  - A goal of more than 80 characters titles its conversation up to a word with "…", not mid-word.
- Approving and finishing were inside "Share or reuse this result" and "Manage task". A "Next step" bar under the draft
  now says what to do. "Approve draft" comes first, then "Mark as done", using the same commands and checks as the
  existing controls, which remain.
- The cockpit's "handoffs and schedules" note became "Read, copy or ask for changes. Approve a draft when you are happy
  with it". The cost line no longer says "Not priced · 1 unpriced".

**Asking for changes**
- The change request page showed the specialist's full internal instructions as plain text. Long plain requests, and
  long owner messages written from a goal, now open with their first lines. The exact text is under "Show the full
  request".
- The run form's allowance was blank and required. It now starts at the same default as Home: 1 credit, or 5 US cents
  for API accounts.
- The confirmation step now says "Writes a new version with your changes" and shows the requested changes first. Its
  consent says the limits are "targets, not hard caps" instead of "soft and can be exceeded upstream".
- The empty manual editor on a waiting request is labelled "Or write the draft yourself (optional)".
- The change form's wording is plainer, and its placeholder gives examples.

**Chat**
- A reply's allowance started blank and required, as above, and now has the same default.
- A change request carries no message of its own, so it always sat at the end of the chat. A later question and its
  reply card then appeared above it, off screen. Work now sits after the latest shown message at the point it was
  created.
- AI replies showed Markdown asterisks. They are now rendered as formatted text.
- Reply authorization still requires opening the review first. That lock is deliberate and covered by a browser test.

**Coming back**
- A browser that remembered a request the company no longer had showed only "Reload saved work", which could not help.
  "Start a new goal" now sits beside it.
- After a draft is approved and done, Home says so and offers "Start another goal" first.

The button labels that browser tests rely on are unchanged: Generate my first draft, Refine draft, Create revision
request, Approve result, Complete after review, Prepare job for review, Review task and context, Authorize and run one
job, Continue my work and Start another goal.

## Checks

- Typecheck, build, formatting and the 52 frontend unit tests pass, as do 39 goal-start, weekly-plan and business-draft
  xUnit tests. A new fact covers shortening a long goal at a word.
- The goal-start, measured and walkthrough browser suites pass.
- The walkthrough suite had started failing with the [studio round](2026-10-30-studio-ux.md): at 1024 × 768 the
  composer ended 34 pixels below the window. The action row's minimum height now applies only to windows at least 860
  pixels tall, so on short screens the row shrinks as designed.
- The business-drafting suite fails identically on a build from before the studio round, so that failure predates both
  rounds.

## Still rough

- Asking for changes takes four steps: write the changes, create the request, prepare it, then authorize the run.
  Each step is now explained in plain words.
- A chat reply takes four clicks: Prepare, Review, tick, Send.
- Settings, documents, memory and recurring work were not walked in this pass.
- The draft content can still carry an internal note into the posts, such as "Reversible assumption: these are written
  for LinkedIn". That belongs to the Marketing instructions, not the interface.
