import { useEffect, useState, type ReactNode } from "react";

const foldEvent = "hirezero:fold";

// Long output stays readable without taking over the conversation (owner feedback, 2026-11-06): a long reply shows its
// first lines and Show all; Collapse all and Expand all in the desk reach every fold at once.
export function Fold({
  children,
  long,
  initiallyOpen = !long,
}: {
  children: ReactNode;
  long: boolean;
  initiallyOpen?: boolean;
}) {
  const [open, setOpen] = useState(initiallyOpen);
  useEffect(() => {
    if (!long) return;
    const all = (event: Event) => setOpen((event as CustomEvent<boolean>).detail);
    window.addEventListener(foldEvent, all);
    return () => window.removeEventListener(foldEvent, all);
  }, [long]);
  if (!long) return <>{children}</>;
  return (
    <div className={"co-fold" + (open ? " open" : "")}>
      <div className="co-fold-body">{children}</div>
      <button type="button" className="co-fold-toggle" aria-expanded={open} onClick={() => setOpen(!open)}>
        {open ? "Show less" : "Show all"}
      </button>
    </div>
  );
}

export function isLong(body: string) {
  return body.length > 700 || body.split(/\r?\n/).length > 14;
}

// Collapse closes every fold and disclosure in the thread; expand opens folds and results, not receipts or settings.
export function foldAll(open: boolean, root: HTMLElement | null) {
  window.dispatchEvent(new CustomEvent(foldEvent, { detail: open }));
  root
    ?.querySelectorAll<HTMLDetailsElement>(open ? "details.co-artifact, details.co-desk-fold" : "details")
    .forEach((details) => (details.open = open));
}
