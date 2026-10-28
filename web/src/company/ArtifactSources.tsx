import { useEffect, useState, type ReactNode } from "react";
import { api, company } from "../lib/api";
import type { Artifact, MeasuredEntry } from "./api";
import { TaskBusinessContext, type TaskBusinessContextData } from "./TaskBusinessContext";

type SavedPacket = {
  attempt: { id: string; taskId: string; measured: { digest: string } };
  call: { context: TaskBusinessContextData & { id: string } };
};

export function ArtifactSources({
  artifact,
  entry,
  children,
}: {
  artifact: Artifact;
  entry?: MeasuredEntry;
  children: ReactNode;
}) {
  const [open, setOpen] = useState(false);
  const [packet, setPacket] = useState<SavedPacket>();
  const [loadedKey, setLoadedKey] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const [retry, setRetry] = useState(0);
  const companyId = company();
  const sourceKey = `${companyId}:${artifact.contextId}:${artifact.taskId}:${entry?.id}:${entry?.job.digest}`;
  useEffect(() => {
    let current = true;
    setPacket(undefined);
    setError("");
    setLoading(false);
    if (open && artifact.contextId && entry) {
      setLoading(true);
      void api<SavedPacket>("/company/measured/" + encodeURIComponent(entry.id))
        .then((saved) => {
          // A familiar title is not proof of origin. Bind the view to this artifact's retained context.
          if (
            saved.attempt.id !== entry.id ||
            saved.attempt.taskId !== artifact.taskId ||
            saved.attempt.measured.digest !== entry.job.digest ||
            saved.call.context.id !== artifact.contextId
          )
            throw new Error("This context does not match the saved draft. Refresh before inspecting its sources.");
          if (current) {
            setPacket(saved);
            setLoadedKey(sourceKey);
          }
        })
        .catch((failure: Error) => {
          if (current) setError(failure.message);
        })
        .finally(() => {
          if (current) setLoading(false);
        });
    }
    return () => {
      current = false;
    };
  }, [open, sourceKey, retry]);
  return (
    <details
      className="co-draft-provenance"
      onToggle={(event) => {
        if (event.target === event.currentTarget) setOpen(event.currentTarget.open);
      }}
    >
      <summary>Source and provenance</summary>
      {children}
      {loading && <p role="status">Loading the saved context…</p>}
      {error && (
        <>
          <p role="alert">{error}</p>
          <button type="button" onClick={() => setRetry((value) => value + 1)}>
            Reload saved context
          </button>
        </>
      )}
      {open && packet && loadedKey === sourceKey && <TaskBusinessContext context={packet.call.context} />}
      {open && (!artifact.contextId || !entry) && (
        <p>A readable model-context snapshot is not available for this result.</p>
      )}
    </details>
  );
}
