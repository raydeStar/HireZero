import { useEffect, useRef } from "react";
import { X } from "lucide-react";
import { Modal } from "./Modal";

export type ShortcutActions = {
  go: (place: "home" | "company" | "employees" | "settings" | "plans" | "meetings") => void;
  toggleMode: () => void;
  toggleCockpit: () => void;
  focusMessage: () => void;
  escape: () => boolean;
  help: () => void;
};

export const shortcutList: [string, string][] = [
  ["?", "Show these shortcuts"],
  ["/", "Write a message"],
  ["g then h", "Home"],
  ["g then c", "Company conversation"],
  ["g then p", "People"],
  ["g then s", "Company settings"],
  ["g then l", "Plans"],
  ["g then m", "Meetings"],
  ["w", "Switch between Chat and Work"],
  [".", "Show or hide the cockpit"],
  ["Esc", "Close the open panel or work"],
];

const places = { h: "home", c: "company", p: "employees", s: "settings", l: "plans", m: "meetings" } as const;

// Keyboard shortcuts (UX round 3). They never fire while you type in a field, or with a modifier key held.
export function useShortcuts(actions: ShortcutActions) {
  const latest = useRef(actions);
  latest.current = actions;
  useEffect(() => {
    let pendingG = 0;
    const handler = (event: KeyboardEvent) => {
      // Modal controls own their keys, including native Escape. Never navigate the page behind them.
      if (event.defaultPrevented || document.querySelector("dialog:modal")) {
        pendingG = 0;
        return;
      }
      const a = latest.current;
      if (event.key === "Escape") {
        if (a.escape()) event.preventDefault();
        return;
      }
      const target = event.target as HTMLElement | null;
      if (
        event.ctrlKey ||
        event.metaKey ||
        event.altKey ||
        (target && (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName)))
      )
        return;
      if (pendingG && Date.now() - pendingG < 1200 && event.key in places) {
        pendingG = 0;
        event.preventDefault();
        a.go(places[event.key as keyof typeof places]);
        return;
      }
      pendingG = 0;
      if (event.key === "g") pendingG = Date.now();
      else if (event.key === "?") a.help();
      else if (event.key === "/") a.focusMessage();
      else if (event.key === "w") a.toggleMode();
      else if (event.key === ".") a.toggleCockpit();
      else return;
      event.preventDefault();
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, []);
}

export function ShortcutHelp({ onClose }: { onClose: () => void }) {
  return (
    <Modal label="Keyboard shortcuts" className="co-dialog-layer" onClose={onClose}>
      <section className="co-dialog">
        <header>
          <h2>Keyboard shortcuts</h2>
          <button className="fe-icon-button" aria-label="Close" onClick={onClose} autoFocus>
            <X size={16} />
          </button>
        </header>
        <dl>
          {shortcutList.map(([keys, label]) => (
            <div key={keys}>
              <dt>
                {keys.split(" then ").map((k, i) => (
                  <span key={k}>
                    {i > 0 && " then "}
                    <kbd>{k}</kbd>
                  </span>
                ))}
              </dt>
              <dd>{label}</dd>
            </div>
          ))}
        </dl>
      </section>
    </Modal>
  );
}
