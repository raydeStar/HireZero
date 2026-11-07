import type { ReactNode } from "react";
import { avatarStyle, initials } from "./Home";

// The employee speaking in the chat (docs/adr/0185): a short line in their own voice, built from what really
// happened (an approval, work finishing, a reply on its way). It is not a model reply and is never stored.
export function EmployeeSays({
  name,
  label,
  tag,
  time,
  children,
}: {
  name: string;
  label: string;
  tag?: string;
  time?: string;
  children: ReactNode;
}) {
  return (
    <article className="fe-msg assistant co-says" aria-label={label}>
      <span className="co-avatar co-msg-avatar" aria-hidden="true" style={avatarStyle(name)}>
        {initials(name)}
      </span>
      <div className="fe-msg-body">
        <div className="fe-msg-meta">
          <strong>{name}</strong>
          {tag && <span className="co-ai-tag">{tag}</span>}
          {time && <small>{time}</small>}
        </div>
        <div className="fe-msg-content">{children}</div>
      </div>
    </article>
  );
}

// Typing dots with what the employee is doing right now, from the work's real state.
export function Working({ children }: { children: ReactNode }) {
  return (
    <p className="co-says-working" role="status" aria-live="polite">
      <span className="co-typing" aria-hidden="true">
        <i />
        <i />
        <i />
      </span>
      <span>{children}</span>
    </p>
  );
}

export const clock = (at?: string) =>
  at ? new Date(at).toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" }) : "";
