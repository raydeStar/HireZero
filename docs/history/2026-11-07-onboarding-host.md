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
- Classic empty-company browser checks (company, company-onboarding, company-scope) choose the form, as the owner can.

## Checks

- Browser: `onboarding-browser-smoke` covers the whole path with a fictional site and model, including an owner
  correction, the approved brief, the needs answer reaching the first assignment, and HireZero retiring.
- A real run on a throwaway instance with Vultr is recorded below.
