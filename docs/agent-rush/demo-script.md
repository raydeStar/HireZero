# Demo script · 4 minutes 45 seconds

Record on <https://app.hirezero.app>. Caption hosted shots **Vultr VM · Vultr Serverless Inference**.

Use a fresh Google account in a private window for the sign-up chapters, so the "Create my company" screen and the
$0.50 allowance appear. Every run is a real, paid Vultr request of a few cents. Record a run from start to finish
rather than replaying an approval. Crop email addresses, account pictures and browser bookmarks.

### 0:00–0:20 · The problem (20 s)

**Picture.** The signed-out page at app.hirezero.app.

**Narration.** "Small business owners want an AI marketing hire. But an agent with your accounts and your card can
post, send and spend. How much damage can this one do? With HireZero, the answer is none you didn't approve."

### 0:20–0:55 · A stranger signs up (35 s)

**Picture.** Sign in with Google in the private window. The screen reads "Welcome to HireZero". Click **Create my
company** and hold on the workspace's "Free allowance remaining: $0.50".

**Narration.** "Anyone can sign in with Google and create one company. It comes with a hard allowance of fifty cents,
and the whole install has a daily cap. The money limit is enforced on the server, before any model is called."

### 0:55–1:45 · Describe the business, approve one job (50 s)

**Picture.** Fill in Home: one sentence about the business and one goal, for example three LinkedIn posts for this week.
Open **Run details** and hold on "Vultr · vultr/deepseek-v4.1-flash-1017" and "One private draft · up to $0.05". Tick
the box and press **Generate my first draft**.

**Narration.** "I describe the business and one goal. Before anything runs, HireZero shows exactly what will happen:
which model, on whose account, and the most it can cost. Nothing is posted, sent or bought, and nothing retries on its
own. One tick authorizes this one job, once."

### 1:45–2:25 · The draft (40 s)

**Picture.** Wait out the run (cut the wait), then open **Drafts ready** and scroll the three posts. Show that the
draft needs review. Click **Home**: it is now the Marketing employee's conversation, with the draft in the thread and
the employee's note under it. Press **Collapse all** once to show the thread folding.

*Optional, if a shift ran before recording:* hold on the header's "On shift · 1 of 5 done" and today's round in the
thread, with its jobs and **Do it**. To have one ready, press **Start shift** (it needs an approved goal ladder) and
leave the first round at its default, about five minutes away.

**Narration.** "About half a minute later, the Marketing employee is done. It's a private draft for me to copy, edit or
send back. It hasn't gone anywhere. Publishing would be a separate approval, bound to this exact version."

### 2:25–3:20 · What it cannot do (55 s)

**Picture.** Back on the workspace, show the remaining allowance after the run. Then show the
[sign-up decision](../adr/0181-open-signup-and-durable-spend-caps.md) and
[Vultr-only decision](../adr/0179-hosted-hirezero-on-vultr.md), with one highlighted line each.

**Narration.** "Here's the blast radius. Money: every request reserves its worst case first, and real usage settles
it. If a reply is lost, the money stays reserved and the request is never resent automatically. Tenants: a free
company sees only its own data, gets no shared credentials, and can't invite anyone. Our own review caught that an
invite could have claimed a stranger's sign-in. Providers: this install sends every model call to Vultr, and starting
it with any other provider is refused."

### 3:20–3:50 · Running on Vultr (30 s)

**Picture.** A simple diagram: browser → nginx with TLS → HireZero on loopback → Vultr Serverless Inference, all on
one Vultr Cloud Compute VM. Then the [deployment record](../history/2026-11-05-hosted-on-vultr.md): first run 32
seconds, 9,177 tokens, reserved $0.0163, settled $0.0043.

**Narration.** "It's all one Vultr VM. nginx handles TLS, the app listens only on the machine itself, and the keys are
system credentials, not settings. Our first real job cost less than half a cent."

### 3:50–4:25 · Contained browsing (35 s)

**Picture.** [rush.hirezero.app](https://rush.hirezero.app): the gallery's Choose a License run. Show the refused
Google Fonts and favicon lines and "Teardown: confirmed".

**Narration.** "The same idea applies to the web. Here, Chromium runs under gVisor with networking switched off. A host
broker allows only the site I approved and logs everything it refused. The receipt confirms the browser was torn
down."

### 4:25–4:45 · What we built this week (20 s)

**Picture.** The README section "What existed before Agent Rush", then the
[event changes](https://github.com/raydeStar/HireZero/compare/9f171b2...main).

**Narration.** "HireZero's employees and approvals existed before this week. For Agent Rush we moved it fully onto
Vultr, opened it to the public with hard spending caps, and deployed it. Try it at app.hirezero.app."
