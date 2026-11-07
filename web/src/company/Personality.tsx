import { useEffect, useState } from "react";
import type { Employee } from "./api";

// The employee's personality with the owner (docs/adr/0184-editable-personality.md): the role's soul.md by default,
// editable per employee. It shapes how they talk in chat and morning rounds, never what they may do.
export function Personality({
  employee,
  soul,
  busy,
  canManage,
  run,
}: {
  employee: Employee;
  soul?: string | null;
  busy: boolean;
  canManage: boolean;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
}) {
  const current = employee.personality ?? soul ?? "";
  const [text, setText] = useState(current);
  const [dirty, setDirty] = useState(false);
  const [saved, setSaved] = useState("");
  useEffect(() => {
    if (!dirty) setText(current);
  }, [current, dirty]);
  if (!current && !canManage) return null;
  const save = async (body: string, notice: string) => {
    const result = await run("employee.personality", {
      id: employee.id,
      expectedVersion: employee.version,
      body,
    });
    if (result) {
      setDirty(false);
      setSaved(notice);
    }
  };
  return (
    <section className="co-personality" aria-label={employee.name + "’s personality"}>
      <h4>Personality</h4>
      <p className="co-muted">
        How {employee.name} talks with you in chat and in morning rounds. It changes their tone, never what they’re
        allowed to do, and drafts for your customers keep your business’s voice.
        {employee.personality ? " You’ve edited it." : soul ? " This is the default for their role." : ""}
      </p>
      {canManage ? (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void save(text, "Saved. Their next reply uses it.");
          }}
        >
          <label>
            <span className="co-hidden-label">{employee.name}’s personality</span>
            <textarea
              rows={10}
              maxLength={4000}
              value={text}
              disabled={busy || employee.state === "retired"}
              onChange={(e) => {
                setText(e.target.value);
                setDirty(true);
                setSaved("");
              }}
            />
          </label>
          <div className="co-actions">
            <button
              className="co-primary"
              type="submit"
              disabled={busy || !dirty || !text.trim() || employee.state === "retired"}
            >
              Save personality
            </button>
            {employee.personality && soul && (
              <button
                type="button"
                disabled={busy || employee.state === "retired"}
                onClick={() => void save("", "Back to the default.")}
              >
                Use the default
              </button>
            )}
          </div>
          {saved && (
            <p role="status" className="co-muted">
              {saved}
            </p>
          )}
        </form>
      ) : (
        <p style={{ whiteSpace: "pre-wrap" }}>{current}</p>
      )}
    </section>
  );
}
