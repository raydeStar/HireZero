import { useEffect, useState } from "react";
import type { Employee, Knowledge, Snapshot } from "./api";
import { ContextFromSources } from "./ContextFromSources";
import { valuesOf } from "./contextDraft";
import { humanize } from "./labels";

// Approved context after onboarding (real-use batch, 2026-10-20): a department's shared facts or the company-wide facts,
// edited from the latest saved version rather than from blank, re-draftable from sources, and saved as a new draft the
// owner approves. Nothing here changes what employees use until a version is approved.
export function ContextEditor({
  snapshot,
  departmentId,
  fields,
  drafter,
  busy,
  run,
  refresh,
}: {
  snapshot: Snapshot;
  // null for the company-wide facts.
  departmentId: string | null;
  fields: { id: string; help?: string }[];
  // The employee who drafts from sources; none hides Fill from sources.
  drafter?: Employee;
  busy: boolean;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
  refresh: () => Promise<void>;
}) {
  const versions = snapshot.data.knowledge.filter((k) => k.departmentId === departmentId);
  const latest = versions.at(-1);
  const draft = versions.filter((k) => k.state === "draft").at(-1);
  const [values, setValues] = useState<Record<string, string>>(() => valuesOf(latest));
  const [sourcesOpen, setSourcesOpen] = useState(false);
  const [error, setError] = useState("");
  // Another department (or a newly saved version) starts from its latest saved text.
  useEffect(() => {
    setValues(valuesOf(latest));
    setSourcesOpen(false);
    setError("");
  }, [departmentId, latest?.id, latest?.version]);
  const missing = fields.filter((f) => !values[f.id]?.trim());
  const review = (k: Knowledge, decision: "approve" | "revoke") =>
    void run("knowledge.review", { id: k.id, expectedVersion: k.version, digest: k.digest, decision });

  return (
    <div className="co-context-editor">
      {drafter && !sourcesOpen && (
        <p className="co-muted">
          Start from your website or repositories:{" "}
          <button type="button" className="co-link" onClick={() => setSourcesOpen(true)}>
            Fill from sources
          </button>
        </p>
      )}
      {drafter && sourcesOpen && (
        <ContextFromSources
          snapshot={snapshot}
          employeeId={drafter.id}
          employeeName={drafter.name}
          fields={fields.map((f) => f.id)}
          scope={departmentId === null ? "company" : undefined}
          refresh={refresh}
          onFill={(filled) => setValues({ ...values, ...filled })}
        />
      )}
      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (missing.length) {
            setError("Complete every field. If a fact is unknown, say so explicitly.");
            return;
          }
          setError("");
          // A pending draft is revised in place; otherwise this becomes a new draft beside the approved version.
          void run("knowledge.save", {
            departmentId,
            body: JSON.stringify(Object.fromEntries(fields.map((f) => [f.id, values[f.id].trim()]))),
            ...(draft ? { id: draft.id, expectedVersion: draft.version } : {}),
          });
        }}
      >
        {fields.map((f) => (
          <label key={f.id}>
            {humanize(f.id)}
            {f.help && <small className="co-muted">{f.help}</small>}
            <textarea value={values[f.id] || ""} onChange={(e) => setValues({ ...values, [f.id]: e.target.value })} />
          </label>
        ))}
        {error && <p role="alert">{error}</p>}
        <button type="submit" className="co-primary" disabled={busy}>
          {draft ? "Update the draft" : latest ? "Save as a new draft" : "Save draft"}
        </button>
      </form>
      {versions
        .slice()
        .reverse()
        .map((k) => {
          const saved = valuesOf(k);
          return (
            <article className="co-card" key={k.id}>
              <b>
                {k.state === "approved" ? "Approved" : k.state === "draft" ? "Draft" : humanize(k.state)} · version{" "}
                {k.version}
              </b>
              <dl className="co-context-fields">
                {Object.entries(saved).map(([field, value]) => (
                  <div key={field}>
                    <dt>{humanize(field)}</dt>
                    <dd>{value}</dd>
                  </div>
                ))}
              </dl>
              {k.state === "draft" && (
                <button className="co-primary" disabled={busy} onClick={() => review(k, "approve")}>
                  Approve this version
                </button>
              )}
              {k.state === "approved" && (
                <button disabled={busy} onClick={() => review(k, "revoke")}>
                  Revoke
                </button>
              )}
            </article>
          );
        })}
    </div>
  );
}
