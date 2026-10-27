# 0109 — Qualification engine without bridge networking

Status: accepted for the owner-approved local setup and fictional browser qualification. Application execution remains disabled.

The owner approved the separate environment proposed in ADR 0108. Before installing its Docker packages,
account for WSL 2 distributions sharing a network namespace. A default Docker bridge and firewall changes
could affect unrelated workloads even when the new engine's data and socket belong to another distribution.

The dedicated qualification daemon therefore sets `bridge: none`, `iptables: false`, `ip6tables: false`,
`ip-forward: false` and `ip-masq: false`. Install this configuration before package scripts can first start
the daemon, after verifying and extracting its pinned gVisor bundle. It retains the proposed default/runtime
registration and local Unix socket. This supersedes ADR 0108's original install-then-configure sequence.

The browser adapter already requires `--network=none`, and the candidate build uses `--network=none`.
There are no published ports or networked containers in this experiment. Disabling Docker's firewall management
is specific to this networkless qualification host; it is not a recommendation for ordinary bridge-networked
Docker hosts. Image pulls and locked dependency restore still need ordinary host HTTPS access.

Record network interfaces, forwarding state and available firewall rules before and after the new daemon starts.
Do not modify existing distributions, Desktop configuration, global WSL settings or existing firewall rules.
The source checkout and existing runtime containment checks remain unchanged.

References: [Microsoft WSL architecture](https://learn.microsoft.com/en-us/windows/wsl/about) and
[Docker daemon options](https://docs.docker.com/reference/cli/dockerd/).
