# 0040: The Muse layout for the company workspace

Status: Accepted.

## Context

Before Phase 2B.5j, HireZero's main interface was the Muse layout (`web/src/shell`): an icon rail, a main panel with
Chat and Work modes, a work window that opened beside the chat, and a cockpit pinned on the right that showed at a
glance what was running, what came next, what had just finished and what needed a decision. ADR 0019 retired that
interface with the legacy profiles it served. The company workspace kept the Muse look (`shell.css`) but not its
shape:
- work appears only as cards inside a conversation;
- what needs the owner sits in the Company hub, a click away from every conversation;
- there is no persistent view of what employees are doing.

The owner wants an enterprise-quality product and considers the two-panel Muse layout part of that.

## Decision

- **Rail · conversations · main panel · cockpit.** The workspace keeps its rail and conversation list. Beside the main
  panel, a cockpit is pinned on the right, reusing the Muse styles:
  - it can be resized (240 to 480 pixels) and hidden, and both choices are remembered in this browser;
  - below 1280 pixels it is a sheet that opens from the top bar, with the count of what needs you.
- **The cockpit answers "what is happening, and what do you need to decide?"** It has four sections:
  - **Needs you:** the Today items, highest priority first. Each opens the place to act.
  - **Working on:** work that is running or gathering evidence.
  - **Up next:** work that is queued or blocked.
  - **Just done:** the latest finished, failed or cancelled work.

  Queued, blocked and running work can be cancelled from there, with the same command and version check as
  everywhere else. In an employee's conversation the cockpit shows that employee's work, with "Everyone" to widen
  it. A footer gives spend to date.
- **Chat | Work in the main panel.** A conversation has two modes:
  - **Chat** is the conversation as it is today.
  - **Work** is a board of that conversation's work: the employee's, or everyone's in the company conversation.
    The groups are Needs review, In progress, Up next and Done.

  Opening a piece of work, from the cockpit, the board or the address, shows the **work window**: the same job card
  the conversation shows, with its review, execution and publication steps. On a wide screen it opens beside the chat;
  otherwise it replaces the board, with a way back. The mode and the open work are kept in the address.
- **No new authority.** The cockpit and the work window only arrange existing views and commands. Every approval
  still happens where it did, on the job card, with the same digests and checks.

## Consequences

- What needs the owner, and what employees are doing, is visible from every conversation.
- Work has a home that is not a scroll position in a chat.
- The Company hub keeps Today's full list, priorities, snoozing and the work log.
- The cockpit is the at-a-glance view. Later Muse pieces, such as the request queue's ordering, can return as their
  backends exist.
