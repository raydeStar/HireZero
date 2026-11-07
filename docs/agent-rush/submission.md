# Agent Rush submission · HireZero on Vultr

Draft for the Vultr Agent Rush listing (November 3–8, 2026). Numbers come from the
[deployment record](../history/2026-11-05-hosted-on-vultr.md). Update them if a later run replaces them.

## Title

HireZero — AI employees with a blast radius of zero

## Short description

A small business owner signs in and hires an AI marketing employee that runs on Vultr. It writes real drafts, but it
cannot post, send, spend or retry anything without the owner's exact approval. Every company has a hard spending
cap.

## Long description

Small business owners want help with marketing, but an AI agent with an owner's accounts and card can do a lot of
damage. HireZero turns that around. Each employee drafts work, and the owner approves the exact job before any model
request leaves the server. What any company can use is capped on the server; customers see tokens used, never a bill.

Anyone can try it at [app.hirezero.app](https://app.hirezero.app). Sign in with Google and create a company.
HireZero greets the owner in chat, asks about the business and its goal, reads only the website the owner confirms,
and drafts a company brief that cites its pages. The owner corrects any line and approves it. HireZero then offers a
Marketing partner and shows, before anything runs, exactly what the first draft will do and which model writes it.
One click gets a private draft to review. Each chat reply is the owner's own Send. Nothing is posted, sent or bought, and nothing is
retried on its own.

**Containment is designed in at every layer:**

- **Money.** Each request reserves its worst case before it is sent: all input plus the full output limit, at the
  reviewed price. That reservation counts against the company's lifetime allowance (50 cents) and against a daily
  cap shared by the whole install ($5). After the call, the real usage replaces the reservation. If the outcome is
  unknown, the full reservation stays held until the owner reconciles it.
- **Actions.** Employees prepare drafts. Publishing, sending, merging and spending need a separate approval that is
  bound to the exact version reviewed. An approval can be used only once.
- **Tenants.** Each company sees only its own data. Free companies get no shared integrations or credentials and
  cannot invite other people. A review caught that an invite could otherwise claim a stranger's sign-in, and it was
  fixed before launch.
- **Providers.** In Vultr-only mode, every model purpose goes to Vultr Serverless Inference. Starting with any other
  provider configured is refused.
- **Secrets.** The Vultr key, the sign-in secret and the key that encrypts stored tokens are systemd credentials.
  They are never in environment variables, settings files or the repository.

**On Vultr, November 5.** One Vultr Cloud Compute VM (`vc2-2c-4gb`, Los Angeles) serves the app behind nginx and TLS.
The first real job drafted three LinkedIn posts on `deepseek-v4.1-flash-1017` in 32 seconds, using 2,594 input and
6,583 output tokens. The cap reserved $0.0163 before sending and settled at **$0.0043**.

**What was built during the event.** HireZero's employees, approvals, budgets and hosted sign-in existed before
November 3. The event added the Vultr provider and Vultr-only mode, open sign-up with durable spending caps, the
hosted deployment on Vultr, onboarding in chat with HireZero, fixes found by review and the first real run, and this
public history.
[Before the event](https://github.com/raydeStar/HireZero/commit/9f171b2) ·
[Event changes](https://github.com/raydeStar/HireZero/compare/9f171b2...main)

**A second demo of containment.** [rush.hirezero.app](https://rush.hirezero.app) runs HireZero's isolated browser.
Chromium runs under gVisor with networking disabled, and a host broker allows only the approved site. Vultr's model
drafts the steps and the owner approves them by digest. The receipt shows what was refused and confirms that the
browser was torn down. [hirezero-engine](https://github.com/raydeStar/hirezero-engine)

## Listing fields

**Challenge** · Blast Radius Zero

**Participation mode** · [Owner to confirm online or on-site]

**Tags** · AI agents, small business, marketing, approvals, spending caps, Vultr, .NET, React

**GitHub** · <https://github.com/raydeStar/HireZero> (engine demo: <https://github.com/raydeStar/hirezero-engine>)

**Demo link** · <https://app.hirezero.app>

**Demo application platform** · Vultr Cloud Compute (one VM), with Vultr Serverless Inference for every model call.

**Video** · Recorded from the [demo script](demo-script.md), 4:45.

**Slides** · Outline below. The earlier engine deck needs these slides replaced.

## Slide outline

1. **The problem.** Owners want an AI marketing hire, but an agent with their accounts can post, send and spend.
2. **HireZero.** Employees draft. You approve the exact job; the server caps what any company can use. Live at
   app.hirezero.app.
3. **One job, start to finish.** Sign in, tell HireZero about the business, correct and approve the brief, see the run
   details, say yes, read the draft. Use screenshots from the recording.
4. **Blast radius zero.** Five layers: money, actions, tenants, providers, secrets, one line each.
5. **Running on Vultr.** nginx → app on loopback → Vultr Serverless Inference, with the VM size, model and first-run
   cost ($0.0043).
6. **Built this week.** The pre-event baseline compared with the event changes, plus the isolated browser at
   rush.hirezero.app.
7. **Try it.** app.hirezero.app · github.com/raydeStar/HireZero
