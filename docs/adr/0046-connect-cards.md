# 0046: Connect cards in the conversation

Status: Accepted. Extends 0038 and 0039.

## Context

Connecting an integration meant leaving the conversation for Company settings → Integrations. An employee could not
tell what was connected, so when work needed GitHub or an API key it could only guess. A refused action showed its
reason as grey text, with no way to fix it in place.

The owner asked for the connection to happen where the need comes up:
1. the employee says what it needs in the chat;
2. a card offers the connection;
3. the owner signs in (GitHub's browser approval) or enters a key;
4. it is recorded.

A much earlier profile had a GitHub reconnect button in the cockpit and a repository confirmation card. It was
retired with the legacy profiles (0019) and never asked for keys.

## Decision

- **Employees know what is connected.** A conversational reply's packet carries a connections line: each
  integration's id, name, and whether it is connected (with the reason when it is not). Demo data sources are
  left out. The reply is told it may end with one line `[connect: id]` when it needs one that is not connected.
  It never asks for a password or key, and it cannot connect anything itself.
- **A connection request is a record** (`co_connections`). It holds the integration id and name, the conversation,
  the employee, the reason, and, for an action, the refused task. It is raised in two ways:
  - by a reply's `[connect: id]` line, which is removed from the text the owner reads;
  - by an action refused with `IntegrationMissingException`. The integration names its own id through its status,
    so no role hard-codes an integration.

  Only an integration the company knows, and that is not connected, raises one. A conversation keeps at most one
  open request per integration. Raising one records `connection.requested`.
- **The owner connects from a Connect card** in that conversation:
  - **GitHub:** the GitHub CLI's browser sign-in (the existing endpoint). The card shows the one-time code and
    link, and updates itself once the owner approves. An existing sign-in on the computer is offered as
    "Use this sign-in". GitHub is now listed among the integrations.
  - **An integration with settings or a key:** the card shows that integration's own setup form. That is the same
    form, the same encrypted local secret store and the same `integration.secret` audit as the Integrations
    screen. Keys are never shown again.
  - **Anything else:** "Open setup" goes to Company settings → Integrations.
- **Resolving is owner-only and checked.** `connection.resolve` succeeds only once the integration reports
  active, and records `integration.connected` with the account when there is one. `connection.dismiss` closes the
  card. Neither carries a secret.

## Consequences

- A card is a suggestion: an untrusted reply can only point at a known integration. The owner still signs in or
  enters the key, and nothing happens without that.
- Connecting does not re-run the refused action. "Run again" is a later step.
- Microsoft 365 and Google sign-in cards are later steps; until then, their cards open setup. So is letting an
  outside AI (Claude, Codex) raise a card or point at where a key already lives, through an MCP endpoint. An AI
  never carries a key value.
