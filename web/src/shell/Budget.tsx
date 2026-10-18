import { useEffect, useState, type ReactNode } from "react";
import { api, ApiError } from "./api";

interface BudgetLimit {
  tokens: number | null;
  runs: number | null;
  minutes: number | null;
  taskTokens: number | null;
  taskRuns: number | null;
  taskMinutes: number | null;
  softPercent: number;
  resumedFor: string | null;
}
interface BudgetStatus {
  scope: string;
  // unreported: entries whose provider gave no usage (model calls are counted at a conservative estimate).
  used: { tokens: number; runs: number; minutes: number; calls: number; unreported?: number };
  percent: number;
  state: "unlimited" | "ok" | "warning" | "reached";
  resumed: boolean;
  summary: string;
  softPercent: number;
}
interface BudgetView {
  month: string;
  roles: { role: string; limit: BudgetLimit; status: BudgetStatus }[];
  tasks: { id: string; title: string; limit: BudgetLimit | null; status: BudgetStatus }[];
}
const name = (role: string) => role.charAt(0).toUpperCase() + role.slice(1);
const used = (s: BudgetStatus) =>
  [
    s.used.runs + " run" + (s.used.runs === 1 ? "" : "s"),
    s.used.minutes + " min",
    s.used.tokens
      ? s.used.tokens.toLocaleString() + " tokens"
      : s.used.calls + " model call" + (s.used.calls === 1 ? "" : "s"),
    ...(s.used.unreported ? [s.used.unreported + " without reported usage (estimated)"] : []),
  ].join(" · ");
const stateLabel = (s: BudgetStatus) =>
  s.state === "reached"
    ? s.resumed
      ? "Over, resumed"
      : "Paused"
    : s.state === "warning"
      ? s.percent + "% used"
      : s.state === "ok"
        ? s.percent + "%"
        : "No limit";
const fields: [keyof BudgetLimit, string][] = [
  ["runs", "Coding runs / month"],
  ["minutes", "Coding minutes / month"],
  ["tokens", "Tokens / month"],
  ["taskRuns", "Runs per task"],
  ["taskMinutes", "Minutes per task"],
  ["taskTokens", "Tokens per task"],
];

/** This month's spend per role and the busiest tasks; at a limit new agent work waits until the owner raises it or resumes. */
export function BudgetPanel({ onForbidden }: { onForbidden: () => void }) {
  const [view, setView] = useState<BudgetView | null>(null),
    [error, setError] = useState(""),
    [editing, setEditing] = useState<string | null>(null),
    [busy, setBusy] = useState("");
  // A host without budgets (404) shows no panel rather than an error.
  function failed(cause: unknown) {
    if (cause instanceof ApiError && cause.status === 403) onForbidden();
    else if (!(cause instanceof ApiError && cause.status === 404)) setError((cause as Error).message);
  }
  useEffect(() => {
    let active = true;
    async function load() {
      try {
        const value = await api<BudgetView>("/budgets");
        if (active) {
          setView(value);
          setError("");
        }
      } catch (cause) {
        if (active) failed(cause);
      }
    }
    void load();
    const timer = setInterval(() => {
      if (document.visibilityState === "visible") void load();
    }, 30000);
    return () => {
      active = false;
      clearInterval(timer);
    };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps
  async function send(scope: string, path: string, body: unknown) {
    setBusy(scope);
    setError("");
    try {
      setView(await api<BudgetView>("/budgets/" + encodeURIComponent(scope) + path, path ? "POST" : "PUT", body));
      setEditing(null);
    } catch (cause) {
      failed(cause);
    } finally {
      setBusy("");
    }
  }
  if (!view)
    return error ? (
      <section className="fe-cockpit-section">
        <h3>Budget</h3>
        <p className="fe-alert" role="alert">
          {error}
        </p>
      </section>
    ) : null;
  const row = (scope: string, label: string, status: BudgetStatus, extra?: ReactNode) => (
    <li key={scope} className={"hz-budget-row " + status.state}>
      <div className="hz-budget-line">
        <strong title={label}>{label}</strong>
        <span className="hz-budget-state">{stateLabel(status)}</span>
      </div>
      {status.state !== "unlimited" && (
        <i className="hz-budget-bar" aria-hidden="true">
          <b style={{ width: Math.min(100, status.percent) + "%" }} />
        </i>
      )}
      <small className="fe-muted" title={status.summary}>
        {used(status)}
        {status.state !== "unlimited" ? " · " + status.summary : ""}
      </small>
      <div className="hz-budget-actions">
        {status.state === "reached" && !status.resumed && (
          <button
            className="fe-link"
            disabled={busy === scope}
            title="Lets new work start despite the limit (a role until month end, a task for good)"
            onClick={() => void send(scope, "/resume", {})}
          >
            Resume anyway
          </button>
        )}
        {extra}
      </div>
    </li>
  );
  return (
    <section className="fe-cockpit-section hz-budget" aria-label="Budget">
      <h3>
        Budget <small className="fe-muted">{view.month}</small>
      </h3>
      <ul className="hz-budget-list">
        {view.roles.map((item) =>
          row(
            "role:" + item.role,
            name(item.role),
            item.status,
            <>
              <button className="fe-link" onClick={() => setEditing(editing === item.role ? null : item.role)}>
                {editing === item.role ? "Close" : "Set limits"}
              </button>
              {editing === item.role && (
                <LimitForm
                  limit={item.limit}
                  busy={busy === "role:" + item.role}
                  onSave={(limit) => void send("role:" + item.role, "", limit)}
                />
              )}
            </>,
          ),
        )}
      </ul>
      {view.tasks.length > 0 && (
        <>
          <h3>Busiest tasks</h3>
          <ul className="hz-budget-list">
            {view.tasks.map((task) => row("task:" + task.id, task.title, task.status))}
          </ul>
        </>
      )}
      <p className="fe-muted">
        At a limit, new fix rounds, coding runs and proactive shift work wait; anything running finishes. Your own asks
        are always answered.
      </p>
      {error && (
        <p className="fe-alert" role="alert">
          {error}
        </p>
      )}
    </section>
  );
}

function LimitForm({
  limit,
  busy,
  onSave,
}: {
  limit: BudgetLimit;
  busy: boolean;
  onSave: (value: BudgetLimit) => void;
}) {
  const [value, setValue] = useState<BudgetLimit>(limit);
  const number = (text: string) => (text.trim() === "" ? null : Math.max(0, Math.floor(Number(text))));
  return (
    <form
      className="dev-hours hz-budget-form"
      onSubmit={(event) => {
        event.preventDefault();
        onSave(value);
      }}
    >
      {fields.map(([key, label]) => (
        <label key={key}>
          {label}
          <input
            type="number"
            min={0}
            placeholder="No limit"
            value={value[key] ?? ""}
            onChange={(event) => setValue({ ...value, [key]: number(event.target.value) })}
          />
        </label>
      ))}
      <label>
        Warn at %
        <input
          type="number"
          min={1}
          max={100}
          value={value.softPercent}
          onChange={(event) =>
            setValue({ ...value, softPercent: Math.min(100, Math.max(1, Number(event.target.value) || 80)) })
          }
        />
      </label>
      <button className="primary" type="submit" disabled={busy}>
        Save limits
      </button>
    </form>
  );
}
