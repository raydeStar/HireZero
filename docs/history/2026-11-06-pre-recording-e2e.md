# Pre-recording end-to-end checks · November 6, 2026

Before the owner's own sign-in tests and the Agent Rush recording, three passes over main at 0567b71 (the morning
desk plus free-company teams, deployed to app.hirezero.app with invitations off).

## 1. Real model run on Vultr

A throwaway second copy of the deployed build ran on the VM in local owner mode (`systemd-run`, 127.0.0.1:5091,
scratch data root, a 300-cent daily cap, the Vultr key passed as a systemd credential so it never left the server),
reached through an SSH tunnel. It was stopped and its data deleted afterwards. The business was fictional: an
independent author with a cozy mystery out next month.

| Step | Result | Tokens (in / out, reasoning) |
| --- | --- | --- |
| First goal, "I really need to market my new book. Get 50 pre-orders before launch day." | Draft in about a minute: one pre-order email with subject lines, bracketed fill-ins, no invented offers | 2,041 / 5,931 (5,427) |
| Goal ladder | Grow 50 pre-orders; steps told → page → reminder list → pre-order; work first on one book page and one link | 4,082 / 5,049 (4,063) |
| Morning round (shift of one round, started for today) | Ran at the chosen minute: What changed, Today's focus, two jobs (first drafted), one question, one lesson; it found Thanksgiving and Black Friday ahead of the launch | 1,698 / 1,345 (792) |
| **Do it** on job 2 (Black Friday post) | Draft about 30 s later | 2,357 / 2,805 (2,660) |

HireZero's ledger: 25,308 tokens, **$0.0106** for the four jobs at Vultr's $0.15 / $0.60 per million.

The employee's note followed each stage ("Right now I'm working on…", "I finished… It's ready for you to read",
"2 things need your go-ahead"), and the outcome box filled and focused the goal form.

## 2. Live site, signed out

app.hirezero.app: Home 200; company, commands, invites and join-accept 401 signed out; `/api/join` 403 (invitations
off); `/mcp` POST 403 (GET is the app page); HSTS, CSP with `frame-ancestors 'none'`, nosniff and no-referrer present;
certificate to 2027-02-03; the test port was not reachable from outside. No errors in the service log since the deploy.

## 3. Full local suite

On main at 0567b71: web and .NET builds passed; Node unit tests 55/55; .NET 957 passed, 4 failed, the rest skipped
(PostgreSQL); browser smokes 45 passed, 9 failed.

- The 4 .NET failures came from the free-company teams merge (see Findings) and pass on `fix/invites-inventory`
  (CompanyRetirementTests 6/6; CompanyBrowserTests, CompanySandboxTests, CompanyBrowserRetentionTests 44/44).
- The 9 browser failures (browser-review, business-drafting, company, company-model, company-onboarding, freshness,
  muse-layout, task-cost, walkthrough) fail at the same step on 1a0d245, before the morning desk, so they predate
  today's UI work. They need their own pass.
- muse-layout also checked the sheet at 1100 px, which the morning desk now docks; that step moves to 900 px. It
  cannot be confirmed until the earlier "work window beside the chat" failure is fixed.

## Findings

- **Retirement inventory missed `co_member_invites`** (from the free-company teams merge). The owner's data
  retirement review did not count invitation rows, which hold invitee emails. Fixed on `fix/invites-inventory`.
- **Three schema-rewind tests** did not drop the new table before replaying migrations. Test-only fix on the same branch.
- The note names work by its task title, so it reads "I finished Marketing draft" rather than the goal. Worth a pass
  after the deadline.
- "1 private drafts in all" on a one-round shift; "with your Vultr" in the run text; a doubled full stop in the
  suggested instructions when the goal ends with one.
- While the round ran, Home stayed at "0 of 1 morning round done" until reload. The browser pane was hidden, so this
  may be background-tab throttling; not confirmed either way.
