# 0039: Integrations start without a restart

Status: Accepted. Supersedes the restart rule in [ADR 0038](0038-integration-setup-in-the-app.md).

## Context

ADR 0038 let the owner set up integrations in the app. Integrations still registered their tools only at startup, so
one set up in the app said "Restart HireZero to start using it". At startup a real provider also takes the place of a
fictional stand-in: Google Analytics instead of synthetic analytics, and Google Ads instead of synthetic ads.

## Decision

- **Adopt, don't add.** `ToolRegistry.Adopt(provider, replaces)` registers a provider's tools and replaces any already
  registered under the same ids. Adopting the same provider again changes nothing.
- **A takeover removes the whole stand-in.** When a real provider replaces a stand-in, every tool the stand-in offers
  is removed first. Otherwise a stand-in tool the real provider does not offer would stay registered beside the real
  reads. The case that matters is the synthetic `ads.change`: with Google Ads reads but no change token, it must not
  remain, or an approved change would go to the stand-in.
- **After every setup change**, the Host adopts each integration that is now active into the registry of every open
  company. Companies opened later register the same way at their own start. The startup choice between a real
  provider and its stand-in is updated, so later companies get the real one too.
- **Turning off still needs a restart.** An integration whose settings or secret are removed shows as not active at
  once, and its calls fail closed because it reads settings and secrets at use. Its tools stay registered until
  HireZero restarts. Unregistering live, while a call may be in flight, is not worth the risk for this.

## Consequences

Setting up an integration in the app takes effect immediately: the "restart" message is gone.
