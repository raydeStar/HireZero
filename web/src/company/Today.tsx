import { useEffect, useState } from "react";
import { api } from "../lib/api";
import type { Need as CockpitNeed } from "./Cockpit";
import { useMemberAccess } from "./MemberAccess";
import type { Snapshot } from "./api";
import { WorkContext, workContext } from "./WorkContext";

type Need = CockpitNeed & {
  markVersion: number;
};
type Entry = {
  type: string;
  text: string;
  employee?: string;
  at: string;
  // One piece of work's latest step; its earlier steps that day are folded in (owner walkthrough, 2026-10-19).
  work?: string | null;
  earlier?: { text: string; at: string }[];
};
type CompanyToday = {
  day: string;
  needsYou: Need[];
  happened: Record<string, number>;
  events: number;
  log: Entry[];
  snoozed: number;
};

const shift = (day: string, days: number) => {
  const date = new Date(day + "T12:00:00Z");
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
};

// The company's day (Phase 2B.5i, with the legacy owner tasks and work log ported in 2B.5j): what waits on you, which
// you can prioritize or snooze, and the day's work log, from the company's own records. No model.
export function Today({
  snapshot,
  version,
  busy,
  run,
  open,
  onOpenNeed,
}: {
  snapshot: Snapshot;
  open?: boolean;
  version: number;
  busy: boolean;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
  onOpenNeed?: (need: CockpitNeed) => void;
}) {
  const { canWork } = useMemberAccess();
  const [today, setToday] = useState<CompanyToday | null>(null),
    [day, setDay] = useState<string | null>(null),
    [error, setError] = useState(""),
    [loading, setLoading] = useState(true),
    [retry, setRetry] = useState(0);
  useEffect(() => {
    let current = true;
    setLoading(true);
    setError("");
    api<CompanyToday>("/company/today" + (day ? "?day=" + day : ""))
      .then((result) => {
        if (!current) return;
        setToday(result);
      })
      .catch((e: Error) => current && setError(e.message))
      .finally(() => current && setLoading(false));
    // An earlier day's reply must not show up late to today's meeting.
    return () => {
      current = false;
    };
  }, [version, day, retry]);
  if (error)
    return (
      <div className="co-today">
        <p role="alert">Could not load company activity. {error}</p>
        <div className="co-actions">
          <button type="button" onClick={() => setRetry((value) => value + 1)}>
            Retry activity
          </button>
          {day && (
            <button type="button" onClick={() => setDay(null)}>
              Back to today
            </button>
          )}
        </div>
      </div>
    );
  if (loading || !today) return <p role="status">Loading company activity…</p>;
  const mark = (n: Need, priority: string, snoozedUntil = "") =>
    void run("owner.mark", { id: n.key, expectedVersion: n.markVersion, decision: priority, body: snoozedUntil });
  return (
    <details className="co-today" open={open ?? today.needsYou.length > 0}>
      <summary>Today: {today.needsYou.length ? `${today.needsYou.length} need you` : "nothing needs you"}</summary>
      {today.needsYou.length > 0 && (
        <ul aria-label="Needs you">
          {today.needsYou.map((n) => (
            <li key={n.key}>
              {onOpenNeed ? (
                <button
                  className="co-link"
                  type="button"
                  onClick={() => onOpenNeed(n)}
                  aria-label={["Open " + n.title, workContext(snapshot, n.taskId)].filter(Boolean).join(" · ")}
                >
                  <strong>{n.title}</strong>
                  <WorkContext snapshot={snapshot} taskId={n.taskId} />
                </button>
              ) : (
                <strong>{n.title}</strong>
              )}{" "}
              <span className="co-muted">{n.detail}</span>{" "}
              {canWork && (
                <>
                  <select
                    aria-label={["Priority of " + n.title, workContext(snapshot, n.taskId)].filter(Boolean).join(" · ")}
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
                </>
              )}
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
              {e.work ? e.work + " · " : ""}
              {e.text}
              {e.earlier && e.earlier.length > 0 && (
                <details className="co-log-steps">
                  <summary>
                    {e.earlier.length} earlier {e.earlier.length === 1 ? "step" : "steps"}
                  </summary>
                  <ol>
                    {e.earlier.map((s, j) => (
                      <li key={j}>
                        <span className="co-muted">
                          {new Date(s.at).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" })}
                        </span>{" "}
                        {s.text}
                      </li>
                    ))}
                  </ol>
                </details>
              )}
            </li>
          ))}
        </ol>
      ) : (
        <p className="co-muted">No company events on {today.day}.</p>
      )}
    </details>
  );
}
