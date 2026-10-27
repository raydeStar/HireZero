import { Fragment, useEffect, useRef, useState } from "react";
import { X, Zap } from "lucide-react";
import { actionChoices, runAction, type ActionChoice, type ActionFieldInfo, type Role } from "./api";

// How many actions show as buttons before the rest move into a menu.
const visibleActions = 4;

// Read-only, tool-backed actions an employee's manifest declares (docs/adr/0011-employee-actions.md). The output
// arrives as an artifact for owner review; only approved artifacts can be attached as evidence. They sit in a strip
// above the composer, one button per action, so what an employee can do is visible without opening anything (owner
// feedback, 2026-10-20); a button opens that action's form in place.
export function EmployeeActions({
  role,
  employeeName,
  employeeId,
  threadId,
  disabled,
  refresh,
}: {
  role: Role | undefined;
  employeeName: string;
  employeeId: string;
  threadId: string;
  disabled: boolean;
  refresh: () => Promise<void>;
}) {
  const actions = role?.actions ?? [];
  const [values, setValues] = useState<Record<string, Record<string, string>>>({});
  const [running, setRunning] = useState("");
  const [refreshing, setRefreshing] = useState("");
  const [error, setError] = useState("");
  const [saved, setSaved] = useState<Record<string, { needsRefresh: boolean }>>({});
  const locked = useRef(false);
  // One request ID per attempt, kept until the outcome is known, so a retry never runs the action twice.
  const [attempt, setAttempt] = useState<Record<string, string>>({});
  // One action form at a time, picked by name (owner walkthrough, 2026-10-19); none until a button is pressed.
  const [picked, setPicked] = useState("");
  // Loaded choices by action and field; an empty list means none could be read, so the value is typed instead.
  const [loaded, setLoaded] = useState<Record<string, ActionChoice[]>>({});
  const shown = actions.find((action) => action.id === picked);
  // Loaded when the action is shown, keyed by name, so a dashboard refresh does not read the source again.
  const loads = shown ? shown.fields.filter((f) => f.loadsChoices).map((f) => shown.id + "/" + f.id) : [];
  const loadKey = loads.join(" ");
  useEffect(() => {
    let current = true;
    for (const key of loadKey ? loadKey.split(" ") : []) {
      const [action, field] = key.split("/");
      actionChoices(employeeId, action, field)
        .then((choices) => current && setLoaded((all) => ({ ...all, [key]: choices })))
        .catch(() => current && setLoaded((all) => ({ ...all, [key]: [] })));
    }
    return () => {
      current = false;
    };
  }, [employeeId, loadKey]);
  if (actions.length === 0) return null;
  const choicesOf = (action: string, field: ActionFieldInfo): ActionChoice[] =>
    field.loadsChoices
      ? (loaded[action + "/" + field.id] ?? [])
      : (field.choices ?? []).map((choice) => ({ value: choice, label: choice }));
  // A field with choices starts on its first choice.
  const valueOf = (action: string, field: ActionFieldInfo) =>
    values[action]?.[field.id] ?? choicesOf(action, field)[0]?.value ?? "";
  async function run(action: string, fields: ActionFieldInfo[]) {
    if (disabled || locked.current || saved[action]) return;
    const input = Object.fromEntries(fields.map((field) => [field.id, valueOf(action, field).trim()]));
    if (Object.values(input).some((value) => !value)) {
      setError("Fill in every field.");
      return;
    }
    const requestId = attempt[action] ?? crypto.randomUUID();
    locked.current = true;
    setAttempt({ ...attempt, [action]: requestId });
    setRunning(action);
    setError("");
    try {
      await runAction(requestId, employeeId, threadId, action, input);
      setAttempt((current) => {
        const next = { ...current };
        delete next[action];
        return next;
      });
      setValues((current) => ({ ...current, [action]: input }));
      setSaved((current) => ({ ...current, [action]: { needsRefresh: false } }));
      try {
        await refresh();
      } catch {
        // The result is already served; a slow window must not send the kitchen the same order twice.
        setSaved((current) => ({ ...current, [action]: { needsRefresh: true } }));
      }
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The action did not complete.");
    } finally {
      locked.current = false;
      setRunning("");
    }
  }
  async function refreshSaved(action: string) {
    if (disabled || locked.current || !saved[action]) return;
    locked.current = true;
    setRefreshing(action);
    try {
      await refresh();
      setSaved((current) => ({ ...current, [action]: { needsRefresh: false } }));
    } catch {
      setSaved((current) => ({ ...current, [action]: { needsRefresh: true } }));
    } finally {
      locked.current = false;
      setRefreshing("");
    }
  }
  function startAnother(action: string) {
    if (disabled || locked.current) return;
    setSaved((current) => {
      const next = { ...current };
      delete next[action];
      return next;
    });
    setValues((current) => ({ ...current, [action]: {} }));
    setError("");
  }
  const pick = (id: string) => {
    setPicked(picked === id ? "" : id);
    setError("");
  };
  const more = actions.slice(visibleActions);
  return (
    <section className="co-action-strip" aria-label="Actions">
      <div className="co-action-buttons">
        <span className="co-action-strip-label">
          <Zap size={14} aria-hidden="true" /> {employeeName} can
        </span>
        {actions.slice(0, visibleActions).map((action) => (
          <button
            key={action.id}
            type="button"
            className="co-action-chip"
            aria-pressed={picked === action.id}
            title={action.description}
            onClick={() => pick(action.id)}
          >
            {action.name}
          </button>
        ))}
        {more.length > 0 && (
          <select
            aria-label="More actions"
            className="co-action-more"
            value={more.some((action) => action.id === picked) ? picked : ""}
            onChange={(event) => event.target.value && pick(event.target.value)}
          >
            <option value="">{more.length} more…</option>
            {more.map((action) => (
              <option key={action.id} value={action.id}>
                {action.name}
              </option>
            ))}
          </select>
        )}
      </div>
      {shown &&
        [shown].map((action) => {
          const start = () => void run(action.id, action.fields);
          const confirmed = saved[action.id];
          const blocked = disabled || running !== "" || refreshing !== "";
          const Inputs = confirmed ? "details" : Fragment;
          const controls = (
            <div className="co-action-run">
              {confirmed ? (
                <>
                  {confirmed.needsRefresh && (
                    <button
                      type="button"
                      className="co-primary"
                      disabled={blocked}
                      onClick={() => void refreshSaved(action.id)}
                    >
                      {refreshing === action.id ? "Refreshing…" : "Retry refresh"}
                    </button>
                  )}
                  <button type="button" disabled={blocked} onClick={() => startAnother(action.id)}>
                    Start another run
                  </button>
                </>
              ) : (
                <button type="button" className="co-primary" disabled={blocked} onClick={start}>
                  {running === action.id ? "Gathering…" : "Run"}
                </button>
              )}
              <button
                type="button"
                className="fe-icon-button"
                aria-label={"Close " + action.name}
                title="Close"
                onClick={() => pick(action.id)}
              >
                <X size={15} />
              </button>
            </div>
          );
          return (
            <div key={action.id} className="co-action" role="group" aria-label={action.name}>
              <p>
                <b>{action.name}</b> {action.description} The result is saved for your review before it can be used.
              </p>
              {confirmed && (
                <p role="status">
                  {confirmed.needsRefresh
                    ? "Action saved. The workspace could not refresh. Your result and inputs are kept."
                    : "Action saved. Review its result in this conversation."}
                </p>
              )}
              {confirmed && controls}
              <Inputs>
                {confirmed && <summary>Submitted inputs</summary>}
                {action.fields.map((field) => {
                  const value = valueOf(action.id, field);
                  const change = (text: string) =>
                    setValues({ ...values, [action.id]: { ...values[action.id], [field.id]: text } });
                  // Long fields take several lines (pasted messages, notes): Enter adds a line, Ctrl+Enter runs. Short
                  // fields run on Enter.
                  const multiline = field.maxLength > 200;
                  const choices = choicesOf(action.id, field);
                  return (
                    <label key={field.id}>
                      {field.label}
                      {choices.length ? (
                        <select
                          aria-label={field.label}
                          value={value}
                          disabled={blocked || !!confirmed}
                          onChange={(event) => change(event.target.value)}
                        >
                          {choices.map((choice) => (
                            <option key={choice.value} value={choice.value}>
                              {choice.label}
                            </option>
                          ))}
                        </select>
                      ) : multiline ? (
                        <textarea
                          aria-label={field.label}
                          maxLength={field.maxLength}
                          rows={6}
                          value={value}
                          disabled={blocked || !!confirmed}
                          onChange={(event) => change(event.target.value)}
                          onKeyDown={(event) => {
                            if (event.key !== "Enter" || !(event.ctrlKey || event.metaKey)) return;
                            event.preventDefault();
                            if (!blocked && !confirmed) start();
                          }}
                        />
                      ) : (
                        <input
                          aria-label={field.label}
                          maxLength={field.maxLength}
                          value={value}
                          disabled={blocked || !!confirmed}
                          onChange={(event) => change(event.target.value)}
                          onKeyDown={(event) => {
                            if (event.key !== "Enter") return;
                            event.preventDefault();
                            if (!blocked && !confirmed) start();
                          }}
                        />
                      )}
                    </label>
                  );
                })}
              </Inputs>
              {!confirmed && controls}
            </div>
          );
        })}
      {error && <p className="co-error">{error}</p>}
    </section>
  );
}
