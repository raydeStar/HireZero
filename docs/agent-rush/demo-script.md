# Demo script · 4 minutes 45 seconds

Record on <https://app.hirezero.app>. Caption hosted shots **Vultr VM · Vultr Serverless Inference**.

Use a fresh Google account in a private window for the sign-up chapters, so the "Create my company" screen appears.
A new company opens on HireZero's chat. A customer never sees what the AI costs, only tokens used; the caps are shown
from the operator's side at 2:25. Free sign-up allows one company per Google account, so a
rehearsal uses up that account: record with a second fresh account, or keep a clean rehearsal as the take. Every
reply and draft is a real, paid Vultr request; the whole onboarding is about a cent. Record a run from start to finish
rather than replaying an approval. Crop email addresses, account pictures and browser bookmarks.

Have ready: the business name, its website (or one sentence about it if it has none), one goal for the next month or
two, and what it needs help with most. Pick one line of the brief to correct on camera.

### 0:00–0:20 · The problem (20 s)

**Picture.** The signed-out page at app.hirezero.app.

**Narration.** "Small business owners want an AI marketing hire. But an agent with your accounts and your card can
post, send and spend. How much damage can this one do? With HireZero, the answer is none you didn't approve."

### 0:20–0:40 · A stranger signs up (20 s)

**Picture.** Sign in with Google in the private window. The screen reads "Welcome to HireZero". Click **Create my
company**. Hold on HireZero's "Hi, I'm HireZero!".

**Narration.** "Anyone can sign in with Google and create one company. They never see a bill. Behind the scenes, each
company has a hard model allowance and the whole install has a daily cap, enforced on the server before any model is
called."

### 0:40–1:25 · HireZero learns the business (45 s)

**Picture.** Type the business name and press **Send**. Answer with the website, then the goal; cut each wait for the
reply. Check the details HireZero picked up and press **Look us up**. Cut the wait, then hold on "Here's what I found":
each line names the page it came from. Press **This is wrong** on one line, type the correction and **Save**; the line
now reads as yours. Press **Looks right**.

**Narration.** "HireZero is the first hire: a Vultr model following a short script, in its own words. Every message I
send authorizes one reply, and I can see the tokens each one used. It reads only the website I confirmed, and every line of
the company brief cites its page. If something's wrong, I fix it in my own words."

### 1:25–1:55 · The first teammate, and what Yes will do (30 s)

**Picture.** HireZero asks what you need help with most; answer it. It recommends a Marketing partner. On the offer, hold
on "Yes writes one private draft using Vultr, included in your plan. Nothing is posted, sent or bought, and nothing is
retried on its own." Open **Run details** and hold on "Vultr · vultr/deepseek-v4.1-flash-1017". Press **Yes, bring them
in**.

**Narration.** "Before anything runs, HireZero shows exactly what will happen and which model will do it. Nothing is
posted, sent or bought, and nothing retries on its own. One click authorizes this one job, once."

### 1:55–2:25 · The draft (30 s)

**Picture.** Cut the wait. HireZero steps aside and Home becomes the Marketing partner's conversation, with "I'll start
on…" and then the first draft in the thread. Scroll the draft and show that it needs review.

*Optional, if a shift ran before recording:* hold on the header's "On shift · 1 of 5 done" and today's round in the
thread, with its jobs and **Do it**. To have one ready, press **Start shift** (it needs an approved goal ladder) and
leave the first round at its default, about five minutes away.

**Narration.** "About half a minute later, my Marketing partner's first draft is in, shaped by what I said I need most.
It's a private draft for me to copy, edit or send back. It hasn't gone anywhere. Publishing would be a separate
approval, bound to this exact version."

### 2:25–3:20 · What it cannot do (55 s)

**Picture.** On the workspace, show the cockpit's "Model use" in tokens. Then show the
[sign-up decision](../adr/0181-open-signup-and-durable-spend-caps.md) and
[Vultr-only decision](../adr/0179-hosted-hirezero-on-vultr.md), with one highlighted line each.

**Narration.** "Here's the blast radius. Money: the customer sees tokens, never a bill, and my spend per company is
capped. Every request reserves its worst case first against that allowance and the install's daily cap, and real usage
settles it. If a reply is lost, the money stays reserved and the request is never resent automatically. Tenants: a free
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
Vultr, opened it to the public with hard spending caps, gave new owners a guided start in chat, and deployed it. Try it
at app.hirezero.app."
