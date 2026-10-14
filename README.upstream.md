# HireZero

A local workspace where you hire AI employees, give them shifts, and review what they bring back.
Each employee is a **role** (developer today; marketer, project manager and others next) that you can
hire or fire without touching the rest of the system.

- **Proactive, not just on request.** On shift, roles check their sources (your GitHub PRs, your inbox)
  and queue work. Off shift, they still answer when you ask. Asked while busy, the agent acknowledges and
  finishes its current task first.
- **You stay in charge.** Anything consequential, such as pushing code, merging or sending, waits for your
  explicit approval inside the role. Every queued item has an ✕.
- **Cheap first.** Incoming messages go through rules, then Jev, then task matching, prioritization and
  finally a model turn, and each step runs only if the cheaper one couldn't settle it.

## Run it

Requirements: Windows, .NET SDK 10, Node 22, the GitHub CLI signed in, and an OpenClaw runtime under
`<data root>\Runtime`. Optional: SemaForge on `127.0.0.1:5058`, a Jev key.

```powershell
./tools/Start-HireZero.ps1                         # data in %USERPROFILE%\.hirezero
./tools/Start-HireZero.ps1 -CheckoutRoot repo      # PR checkouts in <repo>\.worktrees
./tools/Stop-HireZero.ps1
```

Open http://127.0.0.1:5064/. Sign the owner's model cell in with `./tools/OpenClawSignIn.ps1 -Owner`.

## Checks

```powershell
dotnet run --project tests/HireZero.Core.Tests        # queue, shifts, schedule, hiring, intake
dotnet run --project tests/HireZero.Developer.Tests   # GitHub diagnosis, fixes, publication, merge gates
dotnet run --project tests/HireZero.Policy.Tests      # preparation, Jev, Wiki screening
dotnet run --project tests/HireZero.Integrations.Tests  # Microsoft 365 connector (fake Graph)
node --test tests/session-hook.test.mjs                 # session hook forwards metadata only
npm run build --prefix web
```

All checks are synthetic: no live model, GitHub or SemaForge calls.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [docs/ROADMAP.md](docs/ROADMAP.md).

## License

MIT. See [LICENSE](LICENSE).
