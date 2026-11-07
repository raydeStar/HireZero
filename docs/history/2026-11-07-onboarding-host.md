# Onboarding with HireZero · November 7, 2026

The owner asked for onboarding to start in the overall chat, since a new owner has no employee yet: HireZero asks the
name and a few questions in a friendly way, reads up on the business, lets the owner correct it, asks what they need,
and offers a Marketing teammate that starts on "yes". Model replies, scripted order, no soul.
Decision record: [0186](../adr/0186-onboarding-host.md).

## What changed

- `src/HireZero.Roles.Host` and `web/src/roles/host`: the HireZero host role and the onboarding screen. A company with
  no team opens on it; "Use the form instead" keeps the old start.
- The flow: business name (one click sends it) → website or a description → goal → **Look us up** with the details
  picked up from the chat, editable → the site is read and the brief drafted → **This is wrong** / **Add** per line,
  **Try again**, **Looks right** → what you need most → **Yes, bring them in** → the Marketing partner starts on a first
  draft and Home becomes their chat. HireZero retires.
- Sites that send `example.com` to `www.example.com` are read at the `www.` address after one automatic retry; a site
  that still can't be read is said plainly, and the brief starts from the owner's words.
- After the first deploy: the offer shows what **Yes** will do before it is pressed, the provider and model and the most
  the first draft can cost, as the classic start's run details did. The demo script and submission now follow this
  opening.
- Later the same day: a customer of the hosted install sees no AI spend in dollars anywhere, only tokens used; the offer
  reads "included in your plan" ([0187](../adr/0187-customers-see-tokens-not-spend.md)).
- Browser checks of the classic start (company, company-onboarding, company-scope, goal-ladder, goal-start,
  marketing-shift, settings-recovery, team) choose the form, as the owner can.

## Checks

- Browser: `onboarding-browser-smoke` covers the whole path with a fictional site and model, including an owner
  correction, the approved brief, the needs answer reaching the first assignment, and HireZero retiring.
- A real run on a throwaway instance with Vultr is recorded below.

## Real run (throwaway instance, Vultr, DeepSeek V4.1 Flash)

A real, public bakery website, typed as its bare address (`example.com` style). The first run showed the redirect
problem: the site sends the bare address to `www.`, the reader refused to follow it, and every brief line came back Unknown with no explanation. Fixed by
the one `www.` retry and a plain "I couldn't read…" note. The second run, end to end:

| Step | Input tokens | Output tokens |
| --- | ---: | ---: |
| Reply: name | 821 | 90 |
| Reply: website | 1,069 | 99 |
| Reply: goal | 1,311 | 69 |
| Company brief from three site pages | 5,335 | 3,210 |
| Reply: "Looks right" → needs question | 2,609 | 100 |
| Reply: needs → Marketing recommendation | 2,850 | 116 |
| Marketing partner's first draft | 2,727 | 2,020 |

The replies followed the script in their own words, the brief was specific and sourced, the needs answer reached the
first assignment, and HireZero retired as the Marketing partner's draft landed. The first run also called the owner
"Owner", the placeholder name of a local owner; employees now skip it. The provider reports no billed amount; at the
pinned route's rates this run is about a cent. The instance and its data were removed afterwards.
