import { useEffect, useState } from "react";
import { api } from "../shell/api";

type Need = { kind: string; title: string; detail: string; employeeId?: string; taskId?: string };
type CompanyToday = { day: string; needsYou: Need[]; happened: Record<string, number>; events: number };

// The company's day (Phase 2B.5i): what waits on you and what happened, from the company's own records. No model.
// Agent-session activity and a written brief come from the Developer's session-brief action and day-brief skill.
export function Today({ version }: { version: number }) {
  const [today, setToday] = useState<CompanyToday | null>(null),
    [error, setError] = useState("");
  useEffect(() => {
    api<CompanyToday>("/company/today")
      .then((result) => {
        setToday(result);
        setError("");
      })
      .catch((e: Error) => setError(e.message));
  }, [version]);
  if (error) return <p role="alert">{error}</p>;
  if (!today) return null;
  const happened = Object.entries(today.happened).slice(0, 6);
  return (
    <details className="co-today" open={today.needsYou.length > 0}>
      <summary>Today: {today.needsYou.length ? `${today.needsYou.length} need you` : "nothing needs you"}</summary>
      {today.needsYou.length > 0 && (
        <ul aria-label="Needs you">
          {today.needsYou.map((n, i) => (
            <li key={i}>
              <strong>{n.title}</strong> <span className="co-muted">{n.detail}</span>
            </li>
          ))}
        </ul>
      )}
      <p className="co-muted">
        {today.events
          ? `${today.events} company event${today.events === 1 ? "" : "s"} on ${today.day}: ` +
            happened.map(([type, count]) => `${type} ${count}`).join(", ")
          : `No company events on ${today.day} yet.`}
      </p>
    </details>
  );
}
