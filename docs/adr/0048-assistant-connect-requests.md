# 0048: Assistants can ask for connections, and point at keys

Status: Accepted. Extends 0046.

## Context

The owner works with outside assistants (Claude Code, Codex) beside HireZero. They wanted those assistants to help
connect integrations, including "handing off" keys.

An assistant that carries a key value is unsafe:
- the key lands in the assistant's context, transcripts and provider logs;
- a malicious page or file can steer the assistant into handing the key to the wrong place.

## Decision

- **HireZero serves an MCP endpoint** at `/mcp` on its local address. It speaks MCP's HTTP transport with plain JSON
  replies (no streaming) and offers three tools:
  - `list_integrations`: each integration's id, name, connected or not and why, and the names of the keys it
    reads. Never a value.
  - `request_connection(integration_id, reason)`: raises a Connect card that names the assistant (as it introduced
    itself) and shows its reason as its words.
  - `suggest_key_source(integration_id, secret_name, env_var, reason)`: says which environment variable already
    holds one of that integration's keys.
- **Assistant cards belong to no conversation.** They wait under Needs you ("Connect X · asked by Claude Code") and
  at the top of Company settings → Integrations. Unknown or already-connected integrations are refused.
- **The owner decides.** A key-source card shows the variable's name, never its value, and warns when the name does
  not mention the integration. On **Use this key**, HireZero:
  1. reads the variable on this computer (user scope first, then the process);
  2. stores it encrypted like a pasted key;
  3. records `integration.secret` with the variable's name and switches the integration on live.

  If more setup is still needed (a model to choose, a site to pick), the card says so and stays open with the
  integration's form.
- **Only local assistants can reach the endpoint.** It exists in local mode only. A request must be a direct
  loopback connection with the exact expected host. Any `Origin` header is refused: browsers always send one, MCP
  clients do not, so no web page can call it. It writes nothing but connection requests.

## Consequences

- An assistant arranges a connection; the owner completes it in HireZero. No assistant ever sends, receives or
  stores a key.
- Only environment variables are supported as key sources. Windows Credential Manager or a password-manager
  reference (`op://…`) would be further, reviewed sources.
- Registering the endpoint with an assistant is the owner's step; for example,
  `claude mcp add --transport http hirezero http://127.0.0.1:5065/mcp`.
