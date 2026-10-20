import { useState } from "react";
import { runAction, type Role } from "./api";

// Read-only, tool-backed actions an employee's manifest declares (docs/adr/0011-employee-actions.md). The output
// arrives as an artifact for owner review; only approved artifacts can be attached as evidence.
export function EmployeeActions({
  role,
  employeeId,
  threadId,
  disabled,
  refresh,
}: {
  role: Role | undefined;
  employeeId: string;
  threadId: string;
  disabled: boolean;
  refresh: () => Promise<void>;
}) {
  const actions = role?.actions ?? [];
  const [values, setValues] = useState<Record<string, Record<string, string>>>({});
  const [running, setRunning] = useState("");
  const [error, setError] = useState("");
  // One request ID per attempt, kept until the outcome is known, so a retry never runs the action twice.
  const [attempt, setAttempt] = useState<Record<string, string>>({});
  // One action form at a time, picked by name (owner walkthrough, 2026-10-19).
  const [picked, setPicked] = useState("");
  if (actions.length === 0) return null;
  const shown = actions.find((action) => action.id === picked) ?? actions[0];
  // A field with fixed choices starts on its first choice.
  const valueOf = (action: string, field: { id: string; choices?: string[] | null }) =>
    values[action]?.[field.id] ?? field.choices?.[0] ?? "";
  async function run(action: string, fields: { id: string; choices?: string[] | null }[]) {
    const input = Object.fromEntries(fields.map((field) => [field.id, valueOf(action, field).trim()]));
    if (Object.values(input).some((value) => !value)) {
      setError("Fill in every field.");
      return;
    }
    const requestId = attempt[action] ?? crypto.randomUUID();
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
      setValues((current) => ({ ...current, [action]: {} }));
      await refresh();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The action did not complete.");
    } finally {
      setRunning("");
    }
  }
  return (
    <details className="co-evidence">
      <summary>Actions</summary>
      {actions.length > 1 && (
        <label className="co-action-picker">
          Action
          <select
            aria-label="Action"
            value={shown.id}
            onChange={(event) => {
              setPicked(event.target.value);
              setError("");
            }}
          >
            {actions.map((action) => (
              <option key={action.id} value={action.id}>
                {action.name}
              </option>
            ))}
          </select>
        </label>
      )}
      {[shown].map((action) => {
        // The panel sits inside the conversation composer's form, and forms cannot nest: a plain group, a button
        // that is not a submit button, and Enter handled here keep the composer from submitting.
        const start = () => void run(action.id, action.fields);
        return (
          <div key={action.id} className="co-action" role="group" aria-label={action.name}>
            <p>
              <b>{action.name}</b> {action.description} The result is saved for your review before it can be used.
            </p>
            {action.fields.map((field) => {
              const value = valueOf(action.id, field);
              const change = (text: string) =>
                setValues({ ...values, [action.id]: { ...values[action.id], [field.id]: text } });
              // Long fields take several lines (pasted messages, notes): Enter adds a line, Ctrl+Enter runs. Short
              // fields run on Enter.
              const multiline = field.maxLength > 200;
              return (
                <label key={field.id}>
                  {field.label}
                  {field.choices?.length ? (
                    <select aria-label={field.label} value={value} onChange={(event) => change(event.target.value)}>
                      {field.choices.map((choice) => (
                        <option key={choice} value={choice}>
                          {choice}
                        </option>
                      ))}
                    </select>
                  ) : multiline ? (
                    <textarea
                      aria-label={field.label}
                      maxLength={field.maxLength}
                      rows={6}
                      value={value}
                      onChange={(event) => change(event.target.value)}
                      onKeyDown={(event) => {
                        if (event.key !== "Enter" || !(event.ctrlKey || event.metaKey)) return;
                        event.preventDefault();
                        event.stopPropagation();
                        if (!disabled && running === "") start();
                      }}
                    />
                  ) : (
                    <input
                      aria-label={field.label}
                      maxLength={field.maxLength}
                      value={value}
                      onChange={(event) => change(event.target.value)}
                      onKeyDown={(event) => {
                        if (event.key !== "Enter") return;
                        event.preventDefault();
                        event.stopPropagation();
                        if (!disabled && running === "") start();
                      }}
                    />
                  )}
                </label>
              );
            })}
            <button type="button" disabled={disabled || running !== ""} onClick={start}>
              {running === action.id ? "Gathering…" : "Run"}
            </button>
          </div>
        );
      })}
      {error && <p className="co-error">{error}</p>}
    </details>
  );
}
