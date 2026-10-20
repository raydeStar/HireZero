import { useCallback, useEffect, useState, type FormEvent } from "react";
import { api } from "../lib/api";
import { executionStep, type ExecutionStep, type Work } from "./api";
import { publicationState } from "./labels";

type Grant = {
  id: string;
  state: string;
  digest: string;
  version: number;
  workspace: string;
  revision: string;
  route: string;
  expiresAt: string;
  review: null | { digest: string; files: { path: string }[] };
  bounds?: { tokens: number; seconds: number; effort: string };
  allowNetwork?: boolean;
};
type Publication = {
  id: string;
  state: string;
  digest: string;
  version: number;
  summary: string;
  expiresAt: string;
  result: string | null;
  reason: string | null;
};
const live = ["proposed", "approved", "dispatched"];
const started = ["proposed", "approved", "committing", "pushing", "published", "unknown"];

// Reviewed execution for any Execution-mode skill (docs/adr/0012-company-publication.md): grant a checkout, approve
// the exact proposal, run it once, review the diff, then, for skills that publish, approve and send the publication.
export function ExecutionSteps({
  task: t,
  publishes,
  busy,
  run,
  refresh,
}: {
  task: Work;
  publishes: boolean;
  busy: boolean;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
  refresh: () => Promise<void>;
}) {
  const [detail, setDetail] = useState<{ executions: Grant[]; publications?: Publication[] } | null>(null),
    [error, setError] = useState(""),
    [checkout, setCheckout] = useState(""),
    [working, setWorking] = useState(false),
    [network, setNetwork] = useState(false);
  const load = useCallback(
    () =>
      api<{ executions: Grant[]; publications?: Publication[] }>("/company/tasks/" + t.id)
        .then((result) => setDetail(result))
        .catch((e: Error) => setError(e.message)),
    [t.id],
  );
  useEffect(() => {
    void load();
  }, [load, t.version]);
  async function step(name: ExecutionStep, id: string) {
    setWorking(true);
    setError("");
    try {
      await executionStep(name, id);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setWorking(false);
      await refresh();
      await load();
    }
  }
  async function command(action: string, fields: Record<string, unknown>) {
    await run(action, fields);
    await load();
  }
  const grant = detail?.executions.at(-1),
    publication = detail?.publications?.filter((p) => started.includes(p.state)).at(-1),
    disabled = busy || working,
    open = !["completed", "canceled", "superseded", "awaiting_review"].includes(t.state);
  return (
    <section className="co-execution" aria-label="Reviewed execution">
      <h4>Reviewed execution</h4>
      {error && <p role="alert">{error}</p>}
      {!t.resource && open && (
        <form
          onSubmit={(e: FormEvent) => {
            e.preventDefault();
            void command("workspace.grant", {
              id: t.id,
              expectedVersion: t.version,
              body: checkout,
              allowNetwork: network,
            });
          }}
        >
          <label>
            Approved checkout (full local path)
            <input required value={checkout} onChange={(e) => setCheckout(e.target.value)} />
          </label>
          <label>
            <input type="checkbox" checked={network} onChange={(e) => setNetwork(e.target.checked)} /> Allow network for
            this run (for example, to restore packages); off by default
          </label>
          <button className="co-primary" disabled={disabled} type="submit">
            Grant checkout
          </button>
        </form>
      )}
      {t.resource && (
        <p className="co-muted">
          Checkout: {t.resource} · network {t.allowNetwork ? "allowed for this run" : "off"}
        </p>
      )}
      {t.resource && open && !(grant && live.includes(grant.state)) && (
        <button disabled={disabled} type="button" onClick={() => void step("prepare", t.id)}>
          Prepare execution proposal
        </button>
      )}
      {grant && (
        <div className="co-step">
          <p>
            Execution {grant.state} · revision {grant.revision.slice(0, 12)} · {grant.route.replace("openai/", "")}
            {grant.state === "proposed" && " · expires " + new Date(grant.expiresAt).toLocaleTimeString()}
          </p>
          {grant.bounds && (
            <small>
              Limits: {grant.bounds.tokens.toLocaleString()} tokens · {grant.bounds.seconds} seconds ·{" "}
              {grant.bounds.effort} effort · network {grant.allowNetwork ? "on" : "off"}
            </small>
          )}
          {grant.state === "proposed" && (
            <div className="co-actions">
              <button
                className="co-primary"
                disabled={disabled}
                type="button"
                onClick={() =>
                  void command("execution.review", {
                    id: grant.id,
                    expectedVersion: grant.version,
                    digest: grant.digest,
                    decision: "approve",
                  })
                }
              >
                Approve this execution
              </button>
              <button
                disabled={disabled}
                type="button"
                onClick={() =>
                  void command("execution.review", {
                    id: grant.id,
                    expectedVersion: grant.version,
                    decision: "revoke",
                  })
                }
              >
                Revoke
              </button>
            </div>
          )}
          {grant.state === "approved" && (
            <button className="co-primary" disabled={disabled} type="button" onClick={() => void step("run", grant.id)}>
              Run once
            </button>
          )}
          {publishes && grant.state === "completed" && (grant.review?.files.length ?? 0) > 0 && !publication && (
            <button disabled={disabled} type="button" onClick={() => void step("propose", grant.id)}>
              Propose publication
            </button>
          )}
        </div>
      )}
      {publication && publicationControls(publication, disabled, step, command)}
    </section>
  );
}

// The publication's next allowed step: approve or revoke at the exact digest, publish once, or check an unknown outcome.
// A plain function (not a component) so it renders inline in both panels.
function publicationControls(
  publication: Publication,
  disabled: boolean,
  step: (name: ExecutionStep, id: string) => Promise<void>,
  command: (action: string, fields: Record<string, unknown>) => Promise<void>,
) {
  return (
    <div className="co-step">
      <p>
        Publication {publicationState(publication.state)}: {publication.summary}
      </p>
      {publication.result && <small>Result {publication.result.slice(0, 12)}</small>}
      {publication.reason && <p className="co-muted">{publication.reason}</p>}
      {publication.state === "proposed" && (
        <>
          <p className="co-muted">Check the exact step above, then approve it. Nothing is sent until you publish it.</p>
          <div className="co-actions">
            <button
              className="co-primary"
              disabled={disabled}
              type="button"
              onClick={() =>
                void command("publication.review", {
                  id: publication.id,
                  expectedVersion: publication.version,
                  digest: publication.digest,
                  decision: "approve",
                })
              }
            >
              Approve this publication
            </button>
            <button
              disabled={disabled}
              type="button"
              onClick={() =>
                void command("publication.review", {
                  id: publication.id,
                  expectedVersion: publication.version,
                  decision: "revoke",
                })
              }
            >
              Revoke
            </button>
          </div>
        </>
      )}
      {publication.state === "approved" && (
        <button
          className="co-primary"
          disabled={disabled}
          type="button"
          onClick={() => void step("publish", publication.id)}
        >
          Publish once
        </button>
      )}
      {publication.state === "unknown" && (
        <>
          <p className="co-muted">The outcome is unknown and will not be retried. Checking only reads.</p>
          <button disabled={disabled} type="button" onClick={() => void step("reconcile", publication.id)}>
            Check whether it was published
          </button>
        </>
      )}
    </div>
  );
}

// Publishing an approved artifact, such as a Wiki draft (docs/adr/0015-publishing-an-approved-artifact.md): propose it
// once it is approved, then approve and publish the exact publication.
export function ArtifactPublication({
  task: t,
  artifacts,
  busy,
  run,
  refresh,
}: {
  task: Work;
  artifacts: { id: string; state: string; provenance: string }[];
  busy: boolean;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
  refresh: () => Promise<void>;
}) {
  const [detail, setDetail] = useState<{ publications?: (Publication & { artifactId: string })[] } | null>(null),
    [error, setError] = useState(""),
    [working, setWorking] = useState(false);
  const load = useCallback(
    () =>
      api<{ publications?: (Publication & { artifactId: string })[] }>("/company/tasks/" + t.id)
        .then((result) => setDetail(result))
        .catch((e: Error) => setError(e.message)),
    [t.id],
  );
  useEffect(() => {
    void load();
  }, [load, t.version]);
  async function step(name: ExecutionStep, id: string) {
    setWorking(true);
    setError("");
    try {
      await executionStep(name, id);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setWorking(false);
      await refresh();
      await load();
    }
  }
  async function command(action: string, fields: Record<string, unknown>) {
    await run(action, fields);
    await load();
  }
  const publications = detail?.publications ?? [],
    publication = publications.filter((p) => started.includes(p.state)).at(-1),
    disabled = busy || working,
    ready = artifacts.filter(
      (a) =>
        a.state === "approved" &&
        // Gathered evidence is never published, except a publishing action's own reading (ADR 0017).
        (!a.provenance.startsWith("action:") || a.provenance === "action:" + t.skill) &&
        !publications.some((p) => p.artifactId === a.id && started.includes(p.state)),
    );
  return (
    <section className="co-execution" aria-label="Publication">
      <h4>Publication</h4>
      {error && <p role="alert">{error}</p>}
      {!publication &&
        ready.map((a) => (
          <button key={a.id} disabled={disabled} type="button" onClick={() => void step("propose", a.id)}>
            Propose publication
          </button>
        ))}
      {!publication && ready.length === 0 && (
        <p className="co-muted">Approve the result above to propose publishing it.</p>
      )}
      {publication && publicationControls(publication, disabled, step, command)}
    </section>
  );
}
