import { useState } from "react";
import { api } from "./api";

export type Triage = {
  requestId: string;
  channel: string;
  safety: string;
  reasons: string[];
  summary: string | null;
  effort: string | null;
  decision: string | null;
  decidedAt: string | null;
  createdAt: string;
};

/** A request someone (or their AI) sent: how risky it sounds, what you'd need to do, and your decision. */
export function TriageCard({
  requestId,
  requester,
  triage,
  agentAllowed,
  onChanged,
}: {
  requestId: string;
  requester: string;
  triage: Triage;
  agentAllowed?: boolean;
  onChanged: () => void;
}) {
  const [busy, setBusy] = useState(""),
    [note, setNote] = useState(""),
    [role, setRole] = useState(""),
    [error, setError] = useState("");
  async function run(kind: string, call: () => Promise<unknown>) {
    setBusy(kind);
    setError("");
    try {
      await call();
      onChanged();
    } catch (cause) {
      setError((cause as Error).message);
    } finally {
      setBusy("");
    }
  }
  const tone = triage.safety === "looks safe" ? "safe" : triage.safety === "needs care" ? "care" : "risky";
  return (
    <div className={"fe-section dev-triage " + tone}>
      <div className="fe-section-head">
        <strong>
          {triage.channel === "ai" ? requester + "'s AI sent this request" : requester + " sent this request"}
        </strong>
        <span className={"fe-pill dev-safety " + tone}>{triage.safety}</span>
        {triage.effort && <span className="fe-pill">{triage.effort} effort</span>}
      </div>
      <p className="fe-muted">Why: {triage.reasons.join("; ")}.</p>
      {!triage.summary && !agentAllowed && (
        <p className="fe-muted">Confirm or allow agent work before requesting a model summary.</p>
      )}
      {triage.summary ? (
        <div className="dev-triage-summary">
          <strong>What you'd need to do</strong>
          <pre>{triage.summary}</pre>
        </div>
      ) : (
        <button
          className="fe-ghost"
          disabled={!!busy || !agentAllowed}
          onClick={() => void run("summary", () => api("/requests/" + requestId + "/triage", "POST", {}))}
        >
          {busy === "summary" ? "Summarizing…" : "Summarize what I'd need to do"}
        </button>
      )}
      {triage.decision ? (
        <p role="status">You {triage.decision} this request.</p>
      ) : (
        <>
          <label>
            Note to {requester} (optional)
            <input value={note} maxLength={1000} onChange={(event) => setNote(event.target.value)} />
          </label>
          <label className="dev-check">
            When confirming
            <select value={role} onChange={(event) => setRole(event.target.value)}>
              <option value="">keep their current role</option>
              <option value="trusted">promote to trusted</option>
              <option value="boss">promote to boss</option>
              <option value="visitor">make them a visitor</option>
            </select>
          </label>
          <div className="dev-shift-actions">
            <button
              className="primary"
              disabled={!!busy}
              onClick={() =>
                void run("accept", () =>
                  api("/requests/" + requestId + "/decide", "POST", {
                    decision: "accept",
                    note: note || null,
                    role: role || null,
                  }),
                )
              }
            >
              {busy === "accept" ? "Confirming…" : role ? "Promote and confirm" : "Confirm"}
            </button>
            <button
              className="fe-ghost"
              disabled={!!busy}
              onClick={() =>
                void run("decline", () =>
                  api("/requests/" + requestId + "/decide", "POST", { decision: "decline", note: note || null }),
                )
              }
            >
              Decline
            </button>
          </div>
          <p className="fe-muted">
            Confirming tells {requester} you're on it and lets the agent help. Anything that changes code still needs
            your approval of a strategy.
          </p>
        </>
      )}
      {error && (
        <p className="fe-alert" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
