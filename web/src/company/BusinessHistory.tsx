import { useEffect, useRef, useState } from "react";
import { api } from "../lib/api";
import type { BusinessProfileHistory, BusinessSource, ProfileField } from "./api";

export function BusinessFieldEvidence({ field, sources }: { field: ProfileField; sources: BusinessSource[] }) {
  const status = { owner: "Owner-provided", sourced: "Source-supported", hypothesis: "Hypothesis", unknown: "Unknown" };
  return (
    <div className="co-business-field-evidence">
      {(field.verification !== "unknown" || field.value !== "Unknown") && <small>{status[field.verification]}</small>}
      {field.citations.map((citation) => {
        const source = sources.find((s) => s.id === citation.sourceId);
        return (
          <div key={citation.sourceId}>
            <blockquote>“{citation.quote}”</blockquote>
            {source ? (
              <small>
                <a href={source.url} target="_blank" rel="noreferrer">
                  {source.title || source.url}
                </a>
                {" · Retrieved "}
                {new Date(source.retrievedAt).toLocaleString()}
                {source.clipped && " · Limited excerpt"}
              </small>
            ) : (
              <small>Saved source details are unavailable in this view.</small>
            )}
          </div>
        );
      })}
    </div>
  );
}

export function BusinessHistory({
  revision,
  labels,
  sources,
}: {
  revision: string;
  labels: Record<string, string>;
  sources: BusinessSource[];
}) {
  const [open, setOpen] = useState(false);
  const [history, setHistory] = useState<BusinessProfileHistory | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const epoch = useRef(0);
  async function load(before?: number) {
    const request = ++epoch.current;
    setBusy(true);
    setError("");
    try {
      const page = await api<BusinessProfileHistory>("/company/business/history" + (before ? `?before=${before}` : ""));
      if (request === epoch.current)
        setHistory((previous) => ({
          ...page,
          items: before ? [...(previous?.items || []), ...page.items] : page.items,
        }));
    } catch (e) {
      if (request === epoch.current) setError((e as Error).message);
    } finally {
      if (request === epoch.current) setBusy(false);
    }
  }
  useEffect(() => {
    if (open) void load();
    return () => {
      epoch.current++;
    };
  }, [open, revision]);
  return (
    <details className="co-business-history" onToggle={(event) => setOpen(event.currentTarget.open)}>
      <summary>Brief history and corrections</summary>
      <p>
        Saved drafts, owner corrections and approvals are kept separately. Earlier edits made before history was enabled
        may be unavailable.
      </p>
      {error && (
        <p className="co-error" role="alert">
          {error}
        </p>
      )}
      <button disabled={busy} onClick={() => void load()}>
        Refresh brief history
      </button>
      {busy && <p role="status">Loading saved history…</p>}
      {history?.items.length === 0 && <p>No brief changes have been recorded yet.</p>}
      <ol>
        {history?.items.map((entry) => (
          <li key={entry.id}>
            <details>
              <summary>
                {entry.origin === "model"
                  ? "Generated draft"
                  : entry.origin === "earlier"
                    ? "Earlier saved brief"
                    : entry.profile.state === "draft"
                      ? "Owner draft saved"
                      : `Brief ${entry.profile.state}`}
                {" · "}
                {new Date(entry.recordedAt).toLocaleString()}
              </summary>
              <p>
                {entry.actorName} · Revision {entry.profile.version} · {entry.profile.state}
              </p>
              {entry.origin === "earlier" && (
                <p>
                  This snapshot was retained when history was enabled for this brief. Its previous edit time was{" "}
                  {new Date(entry.profile.updatedAt).toLocaleString()}.
                </p>
              )}
              <p>
                {entry.changedFields.length
                  ? "Changed fields: " + entry.changedFields.map((key) => labels[key] || key).join(", ")
                  : entry.profile.state === "draft"
                    ? "Saved without changing the facts."
                    : "Review status changed; the facts stayed the same."}
              </p>
              <dl>
                {entry.profile.fields.map((field) => (
                  <div key={field.key}>
                    <dt>{labels[field.key] || field.key}</dt>
                    <dd>
                      {field.value}
                      <BusinessFieldEvidence field={field} sources={sources} />
                    </dd>
                  </div>
                ))}
              </dl>
            </details>
          </li>
        ))}
      </ol>
      {history?.before && (
        <button disabled={busy} onClick={() => void load(history.before!)}>
          Show older brief changes
        </button>
      )}
    </details>
  );
}
