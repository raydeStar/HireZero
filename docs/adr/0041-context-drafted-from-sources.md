# 0041: Department context drafted from sources

Status: Accepted.

## Context

Onboarding asks the owner to write every shared department fact by hand: for Marketing, purpose, audience,
positioning, voice, claims and constraints. Most of it already exists in the owner's website, GitHub repositories and
blog posts. The owner asked for HireZero to read those sources and suggest the fields, then edit and approve them.

Three existing rules stand in the way:
- the measured one-turn path refuses tasks for employees still onboarding;
- the measured policy caps one turn at 4 KiB of application context;
- nothing in HireZero fetches an arbitrary web page.

## Decision

- **Reading sources costs nothing and calls no model.** The owner lists up to five sources.
  - **GitHub repositories** (`owner/repo` or a github.com URL) are read through the existing `github.read` tool and
    the owner's GitHub CLI sign-in: the description and the README. Private repositories work when that sign-in can
    see them.
  - **Web pages** are read through a new `web.read` tool (Read risk) in a new `HireZero.Integrations.Web`. It accepts
    `https` only. It resolves the host and refuses loopback, private, link-local, multicast and other non-public
    addresses before connecting and after every redirect, at most three redirects. It reads at most 2 MB in 15
    seconds, accepts only text and HTML, and returns the title and readable text with scripts, styles and markup
    removed.
  - Each source is clipped to 6,000 characters and the bundle to 20,000. The owner sees which sources were read, and
    why any were not.
- **Drafting is one turn the owner authorizes,** on the same measured path as a reply: prepared without a model call,
  with its exact context reviewable, a spend cap (cents for an API model) and a receipt.
  - It is a new task kind, `context-draft`. It is the only kind allowed for an employee still onboarding, and only
    for the department that employee belongs to.
  - It runs under its own named policy, `CompanyMeasuredPolicy.ContextDraft`: 24 KiB of application context, a
    1,500-token output target and a 120-second deadline. The store and both providers accept exactly the two named
    policies and nothing else.
  - The instructions ask for one JSON object with exactly the role's onboarding field ids. Facts come only from the
    sources, and "Unknown" is written where they are silent. The sources are untrusted data, never instructions.
- **A suggestion, never a fact.** The reply is saved as the employee's message in its private conversation. The
  onboarding form offers "Fill the fields from this draft". Nothing is saved as knowledge until the owner edits,
  saves and approves it through the existing review, so a source can never reach approved context unread.
  - A reply that is not a JSON object with the expected keys is shown as text and fills nothing.

## Consequences

- Onboarding from real sources takes one authorized turn instead of six hand-written paragraphs. The cost is
  bounded: the context draft policy's worst case at the route's price, before anything is sent.
- The web reader is the first general HTTP fetch in HireZero. It is read-only, owner-started, audited as a tool call,
  and guarded against reaching the owner's own network.
- A later step could let employees cite these sources as evidence. Today the sources reach only the draft.
