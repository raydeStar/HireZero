import { useLayoutEffect, useRef, type ReactNode } from "react";

// Let the browser mind the doorway: background controls become inert until this panel closes.
export function Modal({
  label,
  className = "",
  onClose,
  children,
}: {
  label: string;
  className?: string;
  onClose: () => void;
  children: ReactNode;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  useLayoutEffect(() => {
    const dialog = ref.current!;
    dialog.showModal();
    dialog.querySelector<HTMLButtonElement>('button:not(:disabled):not([tabindex="-1"])')?.focus();
    return () => dialog.close();
  }, []);
  return (
    <dialog
      ref={ref}
      className={"fe-sheet co-modal " + className}
      aria-label={label}
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
    >
      <div className="fe-sheet-scrim" aria-hidden="true" onClick={onClose} />
      {children}
    </dialog>
  );
}
