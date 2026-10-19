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
  if (actions.length === 0) return null;
  async function run(action: string, fields: string[]) {
    const input = Object.fromEntries(fields.map((field) => [field, (values[action]?.[field] ?? "").trim()]));
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
      <summary>Gather evidence</summary>
      {actions.map((action) => {
        // The panel sits inside the conversation composer's form, and forms cannot nest: a plain group, a button
        // that is not a submit button, and Enter handled here keep the composer from submitting.
        const start = () =>
          void run(
            action.id,
            action.fields.map((field) => field.id),
          );
        return (
          <div key={action.id} className="co-action" role="group" aria-label={action.name}>
            <p>
              <b>{action.name}</b> {action.description} The result is saved for your review before it can be used.
            </p>
            {action.fields.map((field) => {
              const value = values[action.id]?.[field.id] ?? "";
              const change = (text: string) =>
                setValues({ ...values, [action.id]: { ...values[action.id], [field.id]: text } });
              // Long fields take several lines (pasted messages, notes): Enter adds a line, Ctrl+Enter runs. Short
              // fields run on Enter.
              const multiline = field.maxLength > 200;
              return (
                <label key={field.id}>
                  {field.label}
                  {multiline ? (
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
