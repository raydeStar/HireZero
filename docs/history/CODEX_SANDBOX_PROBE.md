# Codex sandbox probe (live step 1), 2026-10-18

You approved a non-inference probe of the pinned Codex binary under the live coding runner's exact sandbox profile
([ADR 0014](../adr/0014-pinned-codex-runtime.md)). It made no sign-in, account request or model call. It used a fresh
empty `CODEX_HOME` and fictional files in a temporary folder, which was removed afterwards. Local paths and raw
receipts are kept out of Git.

## Setup

- **Binary:** `codex-cli 0.163.0-alpha.10.2`, the pinned build (SHA-256 `F6DBF48C…0312`).
- **Launch:** `app-server --stdio` with the runner's arguments: `--strict-config`, `windows.sandbox="mxc"`, the
  `hirezero_execution` profile as the default, the same `-c` hardening, and the coding feature list.
- **Profile:**
  - a fictional git checkout, writable;
  - the run's scratch folder, writable;
  - one named folder standing in for a toolchain, read-only;
  - network off.
- **Not in the profile:** a sibling folder and the sign-in home, each holding a marker file.
- **Environment:** the runner's cleared environment.
- **Checks:** each ran through `command/exec` with `permissionProfile: "hirezero_execution"`.

The effective configuration matched exactly (`config/read`):
- `windows.sandbox` was `mxc`;
- the default profile was `hirezero_execution`;
- the filesystem held only the three roots, with their exact access;
- network was disabled;
- the profile was listed as allowed.

## Results

**Run 1, with the runner's environment as built.** Every command was refused before it started, with "MXC requires
nonempty Unicode host SystemDrive". The cleared environment left out `SystemDrive`. Nothing executed. As built, a
live run would have failed closed, with every shell command refused.

**Run 2, with `SystemDrive` passed through:**

| Check | Expected | Observed |
| --- | --- | --- |
| Read a file in the checkout | allowed | allowed (exit 0, marker read) |
| Write a file in the checkout | allowed | allowed (exit 0, file created) |
| Write in the run's scratch folder | allowed | allowed (exit 0, file created) |
| Read the named read-only folder | allowed | allowed (exit 0, marker read) |
| Write the named read-only folder | denied | denied (exit 1, no file) |
| Read a sibling folder | denied | denied (exit 1, marker not read) |
| Write a sibling folder | denied | denied (exit 1, no file) |
| Read the sign-in home | denied | denied (exit 1, marker not read) |
| Connect to an owned loopback listener | denied | denied (curl exit 7; listener saw 0 connections) |
| `git status` in the checkout | allowed | **denied (exit 128)** |
| Read a Windows system file (informational) | n/a | allowed |

`git status` failed because git read the owner's `~/.gitconfig`. The sandbox correctly denied that read, but git
treats the error as fatal.

**Run 3, with an empty git configuration in the scratch folder** (`GIT_CONFIG_GLOBAL`, `GIT_CONFIG_NOSYSTEM=1`):
all ten checks matched expectations, including `git status` (exit 0). Git still warned that it could not read
`~/.config/git/ignore`, which is harmless; pointing `XDG_CONFIG_HOME` into scratch removes it.

## Fixes made from this probe

- `CodexAppServer` passes `SystemDrive` through the cleared environment.
- `CompanyCodexCodingRunner` gives each run an empty git configuration and `XDG_CONFIG_HOME` inside its own scratch
  folder.
- FakeCodex now refuses a launch without them. A mutation check confirmed the live-run test fails if `SystemDrive`
  is dropped.

## Notes for step 2 (one coding turn)

- **System files are readable and executable.** The sandbox allowed reading a Windows system file, and git ran from
  outside the named folders. Programs on the system can run, which is what lets build tools work; only the user's
  files outside the profile were denied. Treat the named folders as extra read access, not the only access.
- **Network:** only loopback was tested, deliberately; no outside host was contacted. Coverage of other destinations
  is inferred from the profile, not observed.
- **Item types.** The 0.163 protocol schema lists item types the runner currently stops on: `plan`,
  `contextCompaction` and `functionCallOutput`. If step 2 or a long run emits them, the run stops safely, and
  allowing them is a separate decision.
- **Protocol fields.** Every field the runner sends to and expects from `thread/start` and `turn/start` is present
  in the 0.163 schema.
- **Probe location.** The probe ran under the system temp folder. Codex warned that it would not create its helper
  aliases there. A real data root outside the temp folder avoids the warning; it did not affect these checks.
