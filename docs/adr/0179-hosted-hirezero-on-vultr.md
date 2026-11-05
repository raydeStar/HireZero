# 0179 — Hosted HireZero on Vultr for Agent Rush

Status: accepted, 2026-11-05, at the owner's direction.

## Context

The owner's Vultr Agent Rush entry (Blast Radius Zero, submissions close 2026-11-08 15:00 America/Denver) must run on
a Vultr VM, serve a web app from it and send every model call to Vultr Serverless Inference. A separate public engine
repository (`raydeStar/hirezero-engine`) already proves the contained browser on that VM with Vultr inference, but it
is a single-question demo. The owner wants customers to use the full HireZero app instead. Codex OAuth and OpenRouter
exist in HireZero for local testing only.

## Decision

- **The full app, hosted.** HireZero runs in hosted mode (`--company-hosted`, [0020](0020-members-roles-and-sign-in.md),
  [0021](0021-companies-per-data-root.md)) on the Vultr VM, behind nginx with TLS, under its own subdomain of
  hirezero.app. Sign-in is Google through generic OIDC.
- **Vultr is the only model provider there.** The API model provider gains a `vultr` vendor
  (`vultr/deepseek-v4.1-flash-1017` at `https://api.vultrinference.com/v1`, OpenAI-compatible, `reasoning_effort`
  low, $0.15/$0.60 per million tokens). A Vultr-only setting routes every model purpose to it and refuses Codex,
  OpenRouter, Jev, Anthropic and OpenAI at construction. Roles or purposes that cannot run on it are unavailable.
- **Open sign-up with caps.** In hosted mode, behind an explicit setting, a verified Google user with no membership
  may create one company and becomes its owner. Each such company has a lifetime model-spend cap, and the install has a
  global daily cap, both checked against worst-case reservations before anything is sent.
- **A fresh data root.** The hosted install has its own data root with no owner integrations (Google, GitHub, Stripe,
  site analytics and so on), because integration settings are per data root and would otherwise be shared with every
  signed-up company.
- **Public code.** A rewritten public copy of this repository, after a private-data sweep of its files and history ([0180](0180-public-copy-not-a-flipped-repo.md)).
- **Fallback.** The engine demo at `rush.hirezero.app` stays live until the hosted app is proven on the VM.

## Consequences

Local installs are unchanged: the Vultr vendor and the Vultr-only and open sign-up settings are off by default. Every
model call on the hosted install spends the owner's Vultr credits, so the caps are part of the deployment, not an
afterthought. Browser and script runtimes on the VM need their own qualification on that engine.
