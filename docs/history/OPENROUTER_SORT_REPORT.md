# OpenRouter message sort, 2026-10-18

Date: 2026-10-18. Branch: `feature/employee-contexts`. Plan: [ROADMAP](../ROADMAP.md).

You asked to use your OpenRouter key for the Jev-style message sort. An OpenRouter key can't call Jev itself (Jev is
TypeSafe's own service and model), so this adds OpenRouter as a second classifier for the same sort: a small optional
integration, configured by you, used only when you start it.

## How to set it up

1. Put your key in an environment variable, `HIREZERO_OPENROUTER_API_KEY` (or `OPENROUTER_API_KEY`). HireZero never
   stores it.
2. Choose a model and name it in `openrouter.json` in the company data root:

   ```json
   { "model": "<vendor>/<model>" }
   ```

   No model id is hard-coded; a cheap, fast model is enough for one-word answers.
3. Start the workspace. The Integrations card shows OpenRouter as active, or says what's missing.

## How it works

**Sort recent messages with OpenRouter** (the Developer's `openrouter-sort` action, choosing 1 to 72 hours and mail,
Teams or both):

- reads up to ten recent Microsoft 365 messages;
- sends each as a short excerpt (sender, subject and the first 700 characters) to your model with the legacy intake's
  question: act, review or ignore;
- asks for JSON only, with a reply budget of 60 tokens, temperature 0, and no tools;
- counts only an allowed answer; anything else is shown as unsorted, and answers under 70% confidence count as review;
- shows the usage OpenRouter reported;
- turns the result into evidence for your review.

It refuses before reading anything if the key or model is missing. It is capped at ten messages because the excerpts
leave this computer and OpenRouter bills per token. Nothing is retried.

The Jev sort is unchanged and now shares the same code (`CompanyMessageSort`), so both classifiers answer the same
question and produce the same evidence.

## What changed

- **`HireZero.Tools.OpenRouter`** (contracts only): `openrouter.status` and `openrouter.choose`, both Read. The state
  leaves the computer, as with Jev.
- **`HireZero.Integrations.OpenRouter`:**
  - `OpenRouterToolProvider` takes the key from the environment and the model from the data root, enforces bounds
    (at most 4,000 characters of state, 2 to 8 choices) before sending, and parses only an allowed choice.
  - Errors never include the key.
  - It uses its own private HTTP handler, so the company profile still has no general HTTP client factory.
- **Developer:** `CompanyMessageSort` replaces `CompanyJevSort` and serves `jev-sort` and `openrouter-sort`. The
  manifest declares the OpenRouter tools.
- **Host:** registers the tools only when the key and model are set, and adds OpenRouter to the integrations status.
- **Architecture test:** OpenRouter names stay out of Core.

## Tests

- `OpenRouterToolProviderTests` (3):
  - nothing is sent until a key and a valid model id are set;
  - one bounded request goes to the chat-completions endpoint with the bearer key, the chosen model, a 60-token
    reply budget, temperature 0 and no tools, and only an allowed choice counts;
  - an out-of-list answer is "unrecognized", and a 402 is "needs credits", without the key;
  - oversized requests are refused before sending.
- `Microsoft365CompanyTests.OpenRouterSortsAtMostTenMessagesIntoReviewedEvidence`: refused with nothing read or sent
  until configured; ten requests at most, with the three choices; a 50%-confidence answer is shown as review; the
  total usage is shown.
- The Jev sort test passes unchanged on the shared code.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build HireZero.slnx` | 0 warnings, 0 errors |
| `dotnet test --solution HireZero.slnx` | 236 passed in two consecutive runs |
| CSharpier and web format checks; Node tests | passed; 14 passed |
| All 7 headless Chrome smoke tests; launcher tests | passed; 14 passed |

**Not verified:** a real OpenRouter request. It needs your key in the environment and a model in `openrouter.json`.
The first live sort spends a few hundred tokens at most.
