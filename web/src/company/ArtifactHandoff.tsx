import { useState } from "react";
import type { Artifact, Department, Employee, Handoff, Role, Thread, Work } from "./api";

export function ArtifactHandoff({
  artifact,
  employees,
  departments,
  threads,
  roles,
  receipts,
  busy,
  run,
  open,
}: {
  artifact: Artifact;
  employees: Employee[];
  departments: Department[];
  threads: Thread[];
  roles: Role[];
  receipts: Handoff[];
  busy: boolean;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
  open: (employeeId: string, threadId: string) => void;
}) {
  const [recipient, setRecipient] = useState(""),
    [thread, setThread] = useState(""),
    [skill, setSkill] = useState(""),
    [instructions, setInstructions] = useState("");
  const person = employees.find((e) => e.id === recipient),
    role = roles.find((r) => r.id === person?.roleId),
    targets = employees.filter((e) => e.id !== artifact.employeeId && e.state === "active");
  function choose(id: string) {
    const selected = employees.find((e) => e.id === id),
      definition = roles.find((r) => r.id === selected?.roleId);
    setRecipient(id);
    setThread("");
    setSkill(definition?.executionSkills?.[0] ?? (definition?.skills.includes("chat") ? "chat" : ""));
  }
  async function assign() {
    if (!recipient || !thread || !skill || !instructions.trim() || busy) return;
    const result = (await run("handoff.assign", {
      id: artifact.id,
      expectedVersion: artifact.version,
      digest: artifact.digest,
      employeeId: recipient,
      threadId: thread,
      skill,
      body: instructions,
    })) as { task: Work } | undefined;
    if (result?.task) {
      setInstructions("");
      open(result.task.employeeId, result.task.threadId);
    }
  }
  return (
    <div className="co-handoff">
      {/* Folded until wanted, so an approved result does not open a form in the conversation (UX round 3). */}
      <details className="co-handoff-share">
        <summary>Share with another employee</summary>
        <h4>Share approved work and assign a task</h4>
        <p>
          Only this approved artifact is shared. The sender's private notes and conversation are excluded. The new task
          needs its own execution approval; this action starts no model or repository work.
        </p>
        {artifact.state === "approved" && (
          <form
            onSubmit={(e) => {
              e.preventDefault();
              void assign();
            }}
          >
            <label>
              Handoff recipient
              <select required value={recipient} onChange={(e) => choose(e.target.value)}>
                <option value="">Choose an active employee</option>
                {targets.map((e) => (
                  <option key={e.id} value={e.id}>
                    {e.name} · {departments.find((d) => d.id === e.departmentId)?.name} · {e.roleId}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Recipient conversation
              <select required value={thread} disabled={!recipient} onChange={(e) => setThread(e.target.value)}>
                <option value="">Choose the exact conversation</option>
                {threads
                  .filter((t) => t.employeeId === recipient && (t.scope ?? "employee") === "employee")
                  .map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.title}
                    </option>
                  ))}
              </select>
            </label>
            <label>
              Assigned work type
              <select required value={skill} disabled={!recipient} onChange={(e) => setSkill(e.target.value)}>
                <option value="">Choose work type</option>
                {role?.skills
                  .filter((s) => !s.startsWith("meeting-") && s !== "company-plan")
                  .map((s) => (
                    <option key={s} value={s}>
                      {s}
                    </option>
                  ))}
              </select>
            </label>
            <label>
              Task instructions and acceptance criteria
              <textarea
                required
                maxLength={4000}
                value={instructions}
                onChange={(e) => setInstructions(e.target.value)}
              />
            </label>
            <button disabled={busy || !recipient || !thread || !skill || !instructions.trim()} type="submit">
              Share this artifact and assign work
            </button>
          </form>
        )}
      </details>
      {receipts
        .filter((h) => h.artifactId === artifact.id && h.targetTaskId)
        .map((h) => (
          <article key={h.id}>
            <p>
              {h.state === "revoked" ? "Revoked" : "Shared"} with{" "}
              {employees.find((e) => e.id === h.targetEmployeeId)?.name} for one assigned task, using approved artifact
              version {h.artifactVersion}.
            </p>
            <button onClick={() => open(h.targetEmployeeId, h.targetThreadId)}>Open assigned work</button>
            {h.state === "shared" && (
              <button
                disabled={busy}
                onClick={() => void run("handoff.revoke", { id: h.id, expectedVersion: h.version })}
              >
                Revoke this handoff
              </button>
            )}
          </article>
        ))}
    </div>
  );
}
