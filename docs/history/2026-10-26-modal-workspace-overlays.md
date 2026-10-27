# Keyboard and responsive overlay verification — 2026-10-26

The existing fictional Chrome layout journey reproduced a cockpit defect: after focusing and opening the
compact cockpit button, keyboard focus remained outside the overlay. This failed before the implementation
changed. Shortcut help also lacked enforced background isolation, and global shortcuts could change the route
behind either panel.

Under [ADR 0122](../adr/0122-modal-workspace-overlays.md), both overlays now use native modal dialogs. Their
visible close controls receive focus; the background becomes inert. Escape and backdrop dismissal restore focus,
and workspace shortcuts do not navigate behind a modal. Expanding to the docked layout clears the compact sheet
state, preventing an unexpected reopen when the window narrows again.

## Verification

- TypeScript/Vite production build passed. The existing large-chunk advisory remains.
- The company fixture project built with zero warnings/errors.
- The expanded Chrome layout journey passed using actual keyboard events. It verifies initial focus,
  background focus refusal, Tab/Shift+Tab, ignored background shortcuts, Escape and backdrop return focus,
  compact-to-docked-to-compact transitions and exact cockpit work navigation on a 390-pixel viewport.
- Existing home, chat/work, theme, conversation drawer, company history/reload, assistant setup and CEO hiring
  draft-preservation checks still pass in that journey.
- Desktop shortcut-help and mobile cockpit captures were visually inspected. The mobile cockpit remains within
  the viewport without horizontal overflow. No uncaught browser exceptions were recorded.
- The fixture cleanup now waits for its own host/browser process exits before removing its verified temporary
  folder. Compact logs and captures remain under ignored `test-results/`.

No live models, external providers, runtime activation, deployment or customer data were involved. This improves
the shared review/navigation experience; real customer acceptance and the other three-phase gates remain open.
