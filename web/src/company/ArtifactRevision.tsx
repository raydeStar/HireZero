import { useRef, useState } from "react";
import { company, memberStorage } from "../lib/api";
import type { Artifact } from "./api";
import "./artifact-revision.css";

export function ArtifactRevision({
  artifact,
  canRevise,
  busy,
  run,
}: {
  artifact: Artifact;
  canRevise: boolean;
  busy: boolean;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
}) {
  const key = `hirezero-artifact-feedback-v1:${company() || "local-company"}:${artifact.id}:${artifact.version}`;
  const [open, setOpen] = useState(false);
  const [feedback, setFeedback] = useState(() => {
    try {
      return memberStorage.getItem(key) ?? artifact.reviewFeedback ?? "";
    } catch {
      return artifact.reviewFeedback ?? "";
    }
  });
  const lock = useRef(false);
  function change(value: string) {
    setFeedback(value);
    try {
      memberStorage.setItem(key, value);
    } catch {
      /* The draft still works in memory. */
    }
  }
  async function save() {
    if (busy || lock.current || !feedback.trim()) return;
    lock.current = true;
    try {
      const result = (await run(canRevise ? "artifact.revise" : "artifact.review", {
        id: artifact.id,
        expectedVersion: artifact.version,
        digest: artifact.digest,
        decision: "changes",
        body: feedback.trim(),
      })) as { id?: string } | undefined;
      if (result?.id) {
        try {
          memberStorage.removeItem(key);
        } catch {
          /* Retaining a local draft is harmless. */
        }
        setOpen(false);
      }
    } finally {
      lock.current = false;
    }
  }
  return open ? (
    <form
      className="co-revision-feedback"
      onSubmit={(event) => {
        event.preventDefault();
        void save();
      }}
    >
      <label>
        What should change?
        <textarea
          required
          maxLength={4000}
          rows={4}
          value={feedback}
          onChange={(event) => change(event.target.value)}
          placeholder="What missed the mark, what should stay, and what would make the next draft useful?"
        />
      </label>
      <p className="co-muted">
        {canRevise
          ? "Saves your feedback and creates a separate revision request. Review its model and allowance before authorizing another run. The original draft stays in your history."
          : "Saves your feedback with this result. Plan, recurring and repository work keep their separate revision workflows."}
      </p>
      <div className="co-actions">
        <button type="submit" disabled={busy || !feedback.trim()}>
          {canRevise ? "Create revision request" : "Save requested changes"}
        </button>
        <button type="button" disabled={busy} onClick={() => setOpen(false)}>
          Keep draft and close
        </button>
      </div>
    </form>
  ) : (
    <button type="button" disabled={busy} onClick={() => setOpen(true)}>
      {feedback.trim() ? "Continue requested changes" : "Ask for changes"}
    </button>
  );
}
