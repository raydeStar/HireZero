import { useCallback, useEffect, useRef, useState } from "react";
import { ChevronLeft, ChevronRight, ExternalLink } from "lucide-react";
import { ApiError, api } from "./api";

type Entry = {
  id: string;
  at: string;
  kind: "coding" | "pr-opened" | "pr-merged" | string;
  project: string;
  text: string;
  url: string | null;
  count: number;
};
type Detail = {
  title: string;
  facts: { label: string; value: string; url: string | null }[];
  excerpt: string | null;
  url: string | null;
};
type Day = {
  day: string;
  ownerName: string | null;
  totals: { projects: number; codingSessions: number; pullRequestsOpened: number; pullRequestsMerged: number };
  entries: Entry[];
  notes: string[];
};

const iso = (date: Date) =>
  date.getFullYear() +
  "-" +
  String(date.getMonth() + 1).padStart(2, "0") +
  "-" +
  String(date.getDate()).padStart(2, "0");
const shift = (day: string, by: number) => {
  const [y, m, d] = day.split("-").map(Number);
  return iso(new Date(y, m - 1, d + by));
};
const label = (day: string) => {
  const today = iso(new Date());
  if (day === today) return "today";
  if (day === shift(today, -1)) return "yesterday";
  const [y, m, d] = day.split("-").map(Number);
  return new Date(y, m - 1, d).toLocaleDateString([], { weekday: "long", month: "short", day: "numeric" });
};
const clock = (at: string) => new Date(at).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
const kindLabel: Record<string, string> = { coding: "Coding", "pr-opened": "Opened", "pr-merged": "Shipped" };

/** At a glance: what the owner did on a day, from the sanitized boss log (company projects, coding activity, PRs). */
export function WorkLogGlance({ onForbidden, preview }: { onForbidden: () => void; preview?: boolean }) {
  const today = iso(new Date());
  const [day, setDay] = useState(today),
    [data, setData] = useState<Day | null>(null),
    [error, setError] = useState("");
  // Callers may pass a new function each render; keep the latest without reloading.
  const forbidden = useRef(onForbidden);
  forbidden.current = onForbidden;
  const [open, setOpen] = useState<string | null>(null),
    [details, setDetails] = useState<Record<string, Detail | "loading" | "missing">>({});
  async function toggle(entry: Entry) {
    if (open === entry.id) {
      setOpen(null);
      return;
    }
    setOpen(entry.id);
    const key = day + "|" + entry.id;
    if (details[key]) return;
    setDetails((current) => ({ ...current, [key]: "loading" }));
    try {
      const value = await api<Detail>("/worklog/detail?day=" + day + "&id=" + encodeURIComponent(entry.id));
      setDetails((current) => ({ ...current, [key]: value }));
    } catch (cause) {
      if (cause instanceof ApiError && cause.status === 403) forbidden.current();
      setDetails((current) => ({ ...current, [key]: "missing" }));
    }
  }
  const load = useCallback(async () => {
    try {
      setError("");
      setData(await api<Day>("/worklog?day=" + day));
    } catch (cause) {
      if (cause instanceof ApiError && cause.status === 403) forbidden.current();
      else setError((cause as Error).message);
    }
  }, [day]);
  useEffect(() => {
    setData(null);
    setOpen(null);
    void load();
  }, [load]);
  useEffect(() => {
    if (day !== today) return;
    const timer = setInterval(() => {
      if (document.visibilityState === "visible") void load();
    }, 300000);
    return () => clearInterval(timer);
  }, [day, today, load]);
  const who = data?.ownerName?.split(" ")[0] || "The owner";
  const totals = data?.totals;
  return (
    <section className="fe-cockpit-section dev-worklog" aria-label="At a glance">
      <div className="dev-worklog-head">
        <h3>{preview ? "Your day, as your boss sees it" : "What " + who + " did"}</h3>
        <div className="dev-worklog-nav">
          <button
            className="fe-icon-button"
            aria-label="Previous day"
            onClick={() => setDay((value) => shift(value, -1))}
          >
            <ChevronLeft size={15} />
          </button>
          <input
            type="date"
            aria-label="Day"
            value={day}
            max={today}
            min={shift(today, -60)}
            onChange={(event) => event.target.value && setDay(event.target.value)}
          />
          <button
            className="fe-icon-button"
            aria-label="Next day"
            disabled={day >= today}
            onClick={() => setDay((value) => shift(value, 1))}
          >
            <ChevronRight size={15} />
          </button>
        </div>
      </div>
      <p className="fe-muted dev-worklog-day">{label(day)}</p>
      {error && <p className="fe-alert">{error}</p>}
      {!data ? (
        <p className="fe-muted" role="status">
          Loading…
        </p>
      ) : (
        <>
          <div className="dev-worklog-totals">
            <div>
              <strong>{totals!.pullRequestsMerged}</strong>
              <span>shipped</span>
            </div>
            <div>
              <strong>{totals!.pullRequestsOpened}</strong>
              <span>PRs opened</span>
            </div>
            <div>
              <strong>{totals!.codingSessions}</strong>
              <span>coding sessions</span>
            </div>
            <div>
              <strong>{totals!.projects}</strong>
              <span>projects</span>
            </div>
          </div>
          {data.entries.length ? (
            <ol className="dev-worklog-list">
              {data.entries.map((entry) => {
                const detail = details[day + "|" + entry.id];
                const expanded = open === entry.id;
                return (
                  <li key={entry.id} className={expanded ? "open" : ""}>
                    <button className="dev-worklog-row" aria-expanded={expanded} onClick={() => void toggle(entry)}>
                      <span className={"dev-worklog-kind " + entry.kind}>{kindLabel[entry.kind] ?? entry.kind}</span>
                      <span className="dev-worklog-text">{entry.text}</span>
                      <small className="fe-muted">{clock(entry.at)}</small>
                    </button>
                    {entry.url && (
                      <a
                        className="fe-icon-button"
                        href={entry.url}
                        target="_blank"
                        rel="noopener noreferrer"
                        aria-label="Open the pull request"
                      >
                        <ExternalLink size={12} />
                      </a>
                    )}
                    {expanded && (
                      <div className="dev-worklog-detail">
                        {detail === "loading" || !detail ? (
                          <p className="fe-muted" role="status">
                            Loading details…
                          </p>
                        ) : detail === "missing" ? (
                          <p className="fe-muted">No more details for this one.</p>
                        ) : (
                          <>
                            {detail.facts.length > 0 && (
                              <dl>
                                {detail.facts.map((fact, index) => (
                                  <div key={index}>
                                    <dt>{fact.label}</dt>
                                    <dd>
                                      {fact.url ? (
                                        <a href={fact.url} target="_blank" rel="noopener noreferrer">
                                          {fact.value}
                                        </a>
                                      ) : (
                                        fact.value
                                      )}
                                    </dd>
                                  </div>
                                ))}
                              </dl>
                            )}
                            {detail.excerpt && <p className="dev-worklog-excerpt">{detail.excerpt}</p>}
                            {detail.url && (
                              <a className="fe-link" href={detail.url} target="_blank" rel="noopener noreferrer">
                                Open the pull request <ExternalLink size={11} />
                              </a>
                            )}
                          </>
                        )}
                      </div>
                    )}
                  </li>
                );
              })}
            </ol>
          ) : (
            <p className="fe-muted">Nothing recorded {label(day) === "today" ? "yet today" : "on this day"}.</p>
          )}
          {data.notes.map((note) => (
            <p key={note} className="fe-muted">
              {note}
            </p>
          ))}
        </>
      )}
    </section>
  );
}
