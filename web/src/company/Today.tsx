import { useEffect, useState } from "react";
import { api } from "../lib/api";

type Need = {
  kind: string;
  title: string;
  detail: string;
  employeeId?: string;
  taskId?: string;
  key: string;
  priority: string;
  markVersion: number;
};
type Entry = { type: string; text: string; employee?: string; at: string };
type CompanyToday = {
  day: string;
  needsYou: Need[];
  happened: Record<string, number>;
  events: number;
  log: Entry[];
  snoozed: number;
};

const shift = (day: string, days: number) => {
  const date = new Date(day + "T12:00:00");
  date.setDate(date.getDate() + days);
  return date.toISOString().slice(0, 10);
};

// The company's day (Phase 2B.5i, with the legacy owner tasks and work log ported in 2B.5j): what waits on you, which
// you can prioritize or snooze, and the day's work log, from the company's own records. No model.
export function Today({
  version,
  busy,
  run,
}: {
  version: number;
  busy: boolean;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
}) {
  const [today, setToday] = useState<CompanyToday | null>(null),
    [day, setDay] = useState<string | null>(null),
    [error, setError] = useState("");
  useEffect(() => {
    api<CompanyToday>("/company/today" + (day ? "?day=" + day : ""))
      .then((result) => {
        setToday(result);
        setError("");
      })
      .catch((e: Error) => setError(e.message));
  }, [version, day]);
  if (error) return <p role="alert">{error}</p>;
  if (!today) return null;
  const mark = (n: Need, priority: string, snoozedUntil = "") =>
    void run("owner.mark", { id: n.key, expectedVersion: n.markVersion, decision: priority, body: snoozedUntil });
  return (
    <details className="co-today" open={today.needsYou.length > 0}>
      <summary>Today: {today.needsYou.length ? `${today.needsYou.length} need you` : "nothing needs you"}</summary>
      {today.needsYou.length > 0 && (
        <ul aria-label="Needs you">
          {today.needsYou.map((n) => (
            <li key={n.key}>
              <strong>{n.title}</strong> <span className="co-muted">{n.detail}</span>{" "}
              <select
                aria-label={"Priority of " + n.title}
                disabled={busy}
                value={n.priority}
                onChange={(e) => mark(n, e.target.value)}
              >
                <option value="high">High</option>
                <option value="normal">Normal</option>
                <option value="low">Low</option>
              </select>{" "}
              <button disabled={busy} type="button" onClick={() => mark(n, n.priority, shift(today.day, 1))}>
                Snooze to tomorrow
              </button>
            </li>
          ))}
        </ul>
      )}
      {today.snoozed > 0 && <p className="co-muted">{today.snoozed} snoozed until a later day.</p>}
      <div className="co-actions" aria-label="Work log day">
        <button type="button" onClick={() => setDay(shift(today.day, -1))}>
          Previous day
        </button>
        <strong>{today.day}</strong>
        {day && (
          <button type="button" onClick={() => setDay(null)}>
            Today
          </button>
        )}
      </div>
      {today.log.length ? (
        <ol className="co-log" aria-label="Work log">
          {today.log.map((e, i) => (
            <li key={i}>
              <span className="co-muted">
                {new Date(e.at).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" })}
              </span>{" "}
              {e.employee ? e.employee + ": " : ""}
              {e.text}
            </li>
          ))}
        </ol>
      ) : (
        <p className="co-muted">No company events on {today.day}.</p>
      )}
    </details>
  );
}
