# Working on HireZero

The plan is [docs/ROADMAP.md](docs/ROADMAP.md); decisions are in [docs/adr](docs/adr/). Record a changed decision as a new ADR.


- Keep the three layers separate: core knows nothing about any role; a role talks to the world only through
  integrations; integrations know nothing about roles. A new employee is a new `src/HireZero.Roles.*` project
  plus a `web/src/roles/*` folder, never edits to another role.
- The repository is public (MIT). Never commit private or employer data: organization or repository names,
  people, emails, tokens, paths under a user profile, or knowledge-base content. Fixtures are fictional.
- Before a large build, image copy or VM test, check free disk space and keep at least 10 GiB free. Clean up
  disposable test output after test processes exit; keep compact logs and receipts.
- Use the smallest checks that cover the change (see README). No GitHub Actions, live model calls or GPU
  inference as part of routine checks.
- Anything consequential (push, merge, send, publish, spend) needs an explicit owner approval in the role,
  and an uncertain external outcome is never replayed automatically.
- Agent runtimes are pinned (`runtime/`). Upgrading one is a reviewed change to its pin and lockfile, never an
  in-place install.
- Format before committing (`dotnet csharpier format .`, `npm run format --prefix web`). New tests are focused xUnit
  facts; dated session notes go in `docs/history/`, not the docs root.
