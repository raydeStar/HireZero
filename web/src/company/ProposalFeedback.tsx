import { useEffect, useRef, useState } from "react";
import { api } from "../lib/api";
import { command, type Artifact } from "./api";

type Direction = {
  id: string;
  version: number;
  digest: string;
  definition: {
    statement: string;
    employeeId?: string;
    proposalFeedback?: { kind: string; subject: string };
  };
};
const key = (value: string) =>
  value
    .toLowerCase()
    .replace(/[^\p{L}\p{N}]+/gu, " ")
    .trim();

export function useProposalFeedback(employeeId: string, cursor: number, changed: () => Promise<void>) {
  const [directions, setDirections] = useState<Direction[]>([]);
  const [ready, setReady] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  const locked = useRef(false);
  useEffect(() => {
    let active = true;
    setReady(false);
    void api<{ memory: Direction & { state: string }; usable: boolean }[]>("/company/memory")
      .then((rows) => {
        if (!active) return;
        setDirections(
          rows
            .filter(
              (v) => v.usable && v.memory.definition.employeeId === employeeId && v.memory.definition.proposalFeedback,
            )
            .map((v) => v.memory),
        );
        setReady(true);
      })
      .catch(() => active && setError("Saved decisions could not be loaded. Refresh before starting a proposal."));
    return () => {
      active = false;
    };
  }, [employeeId, cursor]);
  async function save(artifact: Artifact, kind: "veto" | "correction", subject: string, statement = "") {
    if (locked.current) return false;
    locked.current = true;
    setBusy(true);
    setError("");
    setStatus("");
    try {
      const saved = (await command("proposal.feedback", {
        id: artifact.id,
        digest: artifact.digest,
        decision: kind,
        name: subject,
        body: statement,
      })) as Direction;
      setDirections((old) => [...old.filter((d) => d.id !== saved.id), saved]);
      setStatus(
        kind === "veto"
          ? "Veto saved. This employee will carry it into future work."
          : "Correction saved for this employee’s future work.",
      );
      await changed();
      return true;
    } catch (e) {
      setError((e as Error).message);
      return false;
    } finally {
      locked.current = false;
      setBusy(false);
    }
  }
  async function undo(direction: Direction) {
    if (locked.current) return;
    locked.current = true;
    setBusy(true);
    setError("");
    try {
      await command("memory.review", {
        id: direction.id,
        expectedVersion: direction.version,
        digest: direction.digest,
        decision: "revoke",
      });
      setDirections((old) => old.filter((d) => d.id !== direction.id));
      setStatus("Decision removed. Nothing has been restarted.");
      await changed();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      locked.current = false;
      setBusy(false);
    }
  }
  return {
    directions,
    ready,
    busy,
    error,
    status,
    save,
    undo,
    veto: (subject: string) =>
      directions.find(
        (d) =>
          d.definition.proposalFeedback?.kind === "veto" && key(d.definition.proposalFeedback.subject) === key(subject),
      ),
  };
}

export function ProposalFeedback({
  artifact,
  employeeName,
  feedback,
}: {
  artifact: Artifact;
  employeeName: string;
  feedback: ReturnType<typeof useProposalFeedback>;
}) {
  const [editing, setEditing] = useState(false);
  const [subject, setSubject] = useState("");
  const [correction, setCorrection] = useState("");
  const { directions, busy, error, status, save, undo } = feedback;
  return (
    <div className="co-proposal-feedback">
      <button
        type="button"
        onClick={() => {
          const selection = window.getSelection()?.toString().trim() || "";
          setSubject(
            selection.length >= 4 && artifact.body.includes(selection)
              ? selection.slice(0, 160)
              : artifact.body
                  .split(/\r?\n/)
                  .find((line) => line.trim().length >= 4)
                  ?.trim()
                  .slice(0, 160) || "",
          );
          setEditing(true);
        }}
      >
        That’s inaccurate
      </button>
      {editing && (
        <form
          className="co-form"
          onSubmit={(e) => {
            e.preventDefault();
            void save(artifact, "correction", subject, correction).then((saved) => {
              if (saved) {
                setEditing(false);
                setCorrection("");
              }
            });
          }}
        >
          <label>
            What should {employeeName} know instead?
            <textarea
              autoFocus
              value={correction}
              maxLength={1200}
              required
              onChange={(e) => setCorrection(e.target.value)}
              placeholder="For example: We only serve local businesses. Stop assuming we sell online."
            />
          </label>
          <p className="co-muted">
            Saves your direction for this employee. The original draft and shared business brief stay unchanged.
          </p>
          <div className="co-actions">
            <button disabled={busy || !correction.trim()}>Save correction</button>
            <button type="button" disabled={busy} onClick={() => setEditing(false)}>
              Cancel
            </button>
          </div>
        </form>
      )}
      {status && <p role="status">{status}</p>}
      {error && <p role="alert">{error}</p>}
      {directions.length > 0 && (
        <details>
          <summary>Remembered decisions ({directions.length})</summary>
          <ul>
            {directions.map((d) => (
              <li key={d.id}>
                <span>{d.definition.statement}</span>{" "}
                <button disabled={busy} onClick={() => void undo(d)}>
                  Undo
                </button>
              </li>
            ))}
          </ul>
          <p className="co-muted">
            These stay in {employeeName}’s instructions until you undo them. No expiry or automatic retry.
          </p>
        </details>
      )}
    </div>
  );
}
