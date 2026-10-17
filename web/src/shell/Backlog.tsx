import { useEffect, useState } from "react";
import { api, ApiError, when } from "./api";

interface Source {
  info: { id: string; title: string; configured: boolean; ready: boolean; missingReason: string | null };
  checkpoint: {
    state: string;
    lastComplete: string | null;
    lastPage: string | null;
    received: number;
    gaps: string[] | null;
    note: string | null;
  };
  freshness: string;
}
interface InboxItem {
  id: string;
  sourceId: string;
  current: boolean;
  unavailableReason: string | null;
  event: { title: string; status: string; sourceUpdatedAt: string; requestId: string | null };
}
interface Proposal {
  id: string;
  title: string;
  status: string;
  revision: string;
  snapshot: string;
  blockers: string[];
  plan: { action: string; provider: string; steps: string[]; transmission: string } | null;
  approvedAt: string | null;
  expiresAt: string | null;
}
interface Dashboard {
  sources: Source[];
  inbox: InboxItem[];
  proposals: Proposal[];
  previousHostSeen: string | null;
  hostSeen: string;
  limits: string[];
}

export function BacklogView({ onOpen, onForbidden }: { onOpen: (id: string) => void; onForbidden: () => void }) {
  const [view, setView] = useState<Dashboard | null>(null),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  const [action, setAction] = useState("manual-review"),
    [provider, setProvider] = useState("codex");
  function failed(cause: unknown) {
    setError((cause as Error).message);
    if (cause instanceof ApiError && cause.status === 403) onForbidden();
  }
  useEffect(() => {
    let active = true;
    api<Dashboard>("/backlog")
      .then((value) => {
        if (active) setView(value);
      })
      .catch((cause) => {
        if (active) failed(cause);
      });
    return () => {
      active = false;
    };
  }, []);
  async function run(work: () => Promise<unknown>) {
    if (busy) return;
    setBusy(true);
    setError("");
    try {
      await work();
      setView(await api<Dashboard>("/backlog"));
    } catch (cause) {
      failed(cause);
    } finally {
      setBusy(false);
    }
  }
  return (
    <div className="fe-stack" aria-label="Local backlog">
      <section className="fe-section">
        <div className="fe-section-head">
          <h2>Local backlog</h2>
          <button disabled={busy} onClick={() => void run(() => api("/backlog/catch-up", "POST", {}))}>
            {busy ? "Checking…" : "Catch up selected sources"}
          </button>
        </div>
        <p className="fe-muted">
          Review saved metadata and proposals while this host is available. Every source starts disabled. Catch-up reads
          at most four pages per source; use it again when a source is still catching up.
        </p>
        {view?.previousHostSeen && (
          <p className="fe-muted">
            Previous host activity recorded {when(view.previousHostSeen)}. This is not a complete offline history.
          </p>
        )}
        {error && (
          <p className="fe-alert" role="alert">
            {error}
          </p>
        )}
        {view?.limits.map((limit) => (
          <p className="fe-muted" key={limit}>
            {limit}
          </p>
        ))}
      </section>
      <section className="fe-section">
        <h3>Sources and freshness</h3>
        {view?.sources.map((source) => (
          <article className="fe-section" key={source.info.id}>
            <div className="fe-section-head">
              <strong>{source.info.title}</strong>
              <span className="fe-pill">{source.freshness}</span>
            </div>
            <p className="fe-muted">
              {source.info.missingReason ??
                "Only metadata already saved in this workspace. No messages, session transcripts or provider APIs are collected."}
            </p>
            <p className="fe-muted">
              Last complete scan: {source.checkpoint.lastComplete ? when(source.checkpoint.lastComplete) : "Never"} ·
              Last saved page: {source.checkpoint.lastPage ? when(source.checkpoint.lastPage) : "None"}
            </p>
            {source.checkpoint.gaps?.map((gap) => (
              <p className="fe-alert" key={gap}>
                {gap}
              </p>
            ))}
            {source.checkpoint.note && <p className="fe-alert">{source.checkpoint.note}</p>}
            {source.info.configured && (
              <button
                disabled={busy}
                onClick={() =>
                  void run(() => api("/backlog/sources/" + source.info.id, "PUT", { enabled: !source.info.ready }))
                }
              >
                {source.info.ready ? "Disable local collection" : "Enable this local metadata source"}
              </button>
            )}
          </article>
        ))}
      </section>
      <section className="fe-section">
        <h3>Review proposals</h3>
        <p className="fe-muted">
          A plan records what you want to review. Provider plans cannot be approved or run. A local approval records
          review intent and starts no agent.
        </p>
        <div className="dev-form-row">
          <label>
            Plan
            <select value={action} onChange={(event) => setAction(event.target.value)}>
              <option value="manual-review">Manual local review</option>
              <option value="diagnose">Diagnosis proposal</option>
              <option value="judge-comments">Comment judgment proposal</option>
              <option value="repair">Repair proposal</option>
            </select>
          </label>
          {action !== "manual-review" && (
            <label>
              Proposed provider
              <select value={provider} onChange={(event) => setProvider(event.target.value)}>
                <option value="codex">Codex — unavailable</option>
                <option value="claude">Claude — unavailable</option>
              </select>
            </label>
          )}
        </div>
        {view?.proposals.map((proposal) => (
          <article className="fe-section" key={proposal.id}>
            <div className="fe-section-head">
              <strong>{proposal.title}</strong>
              <span className="fe-pill">{proposal.status.replaceAll("-", " ")}</span>
            </div>
            <small className="fe-muted">Evidence revision {proposal.revision.slice(0, 12)}</small>
            {proposal.plan && (
              <>
                <ol>
                  {proposal.plan.steps.map((step) => (
                    <li key={step}>{step}</li>
                  ))}
                </ol>
                <p>{proposal.plan.transmission}</p>
              </>
            )}
            {proposal.blockers.map((blocker) => (
              <p className="fe-alert" key={blocker}>
                {blocker}
              </p>
            ))}
            {["proposed", "planned"].includes(proposal.status) && (
              <button
                disabled={busy}
                onClick={() =>
                  void run(() =>
                    api("/backlog/proposals/" + proposal.id + "/plan", "POST", {
                      snapshot: proposal.snapshot,
                      action,
                      provider: action === "manual-review" ? "none" : provider,
                    }),
                  )
                }
              >
                Save review plan
              </button>
            )}
            {proposal.status === "planned" && proposal.plan?.action === "manual-review" && (
              <button
                disabled={busy}
                onClick={() =>
                  void run(() =>
                    api("/backlog/proposals/" + proposal.id + "/approve", "POST", { snapshot: proposal.snapshot }),
                  )
                }
              >
                Record local review approval
              </button>
            )}
            {proposal.approvedAt && (
              <p className="fe-muted">
                Local review approval recorded {when(proposal.approvedAt)}. No execution started.
              </p>
            )}
          </article>
        ))}
        {view?.proposals.length === 0 && <p className="fe-empty">No proposals from selected local sources.</p>}
      </section>
      <section className="fe-section">
        <h3>Recorded metadata</h3>
        <p className="fe-muted">Latest 200 stored revisions. Earlier revisions are marked superseded.</p>
        {view?.inbox.map((item) => (
          <div className="fe-list-row" key={item.id}>
            <span className="fe-list-main">
              <strong>{item.event.title}</strong>
              <small>
                {item.sourceId} · {item.event.status} ·{" "}
                {item.unavailableReason
                  ? "Historical — source unavailable"
                  : item.current
                    ? "Current recorded revision"
                    : "Superseded"}{" "}
                · {when(item.event.sourceUpdatedAt)}
              </small>
              {item.unavailableReason && <small>{item.unavailableReason} No current approval is available.</small>}
            </span>
            {item.event.requestId && <button onClick={() => onOpen(item.event.requestId!)}>Open request</button>}
          </div>
        ))}
      </section>
    </div>
  );
}
