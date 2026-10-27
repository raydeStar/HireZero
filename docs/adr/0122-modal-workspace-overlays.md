# ADR 0122: Modal workspace overlays own keyboard focus

Status: accepted, 2026-10-26. Extends ADR 0040's compact cockpit layout.

## Context

The narrow cockpit used a visually blocking overlay without moving keyboard focus or making the background
inert. Shortcut help declared modal semantics but did not enforce them. Global shortcuts could navigate the
workspace behind either overlay. A retained sheet flag could reopen the compact cockpit after resizing through
the wide layout. These behaviors disrupt review navigation and keyboard use across all three product phases.

## Decision

Use a shared native `dialog` opened with `showModal()` for the compact cockpit and shortcut help. The browser
makes the background inert and restores focus when the dialog closes. Focus the visible close control on entry;
the pointer backdrop is not a separate keyboard stop. Escape, the close control and backdrop dismiss the overlay
without executing work. Keep the docked cockpit as the existing nonmodal sidebar.

While a native modal is open, workspace navigation and mode shortcuts do not run, and any pending two-key
navigation sequence is cleared. Escape belongs to the dialog's cancel event. Returning to the wide layout
clears the compact sheet state, so narrowing the window again does not reopen a previously dismissed overlay.
Selecting a cockpit item still closes the overlay and opens the exact existing destination.

## Verification boundary

Use actual Chrome keyboard input in the existing fictional layout journey, including focus entry/return,
background focus refusal, blocked navigation shortcuts, Tab/Shift+Tab, Escape, backdrop dismissal, resize and
mobile work navigation. Preserve existing chat/work, history, hiring-draft and setup checks. This is local UI
evidence; it grants no work approval and does not establish hosted, provider or customer acceptance.
