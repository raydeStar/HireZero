import { memberStorage as sessionStorage } from "../lib/api";
import { useEffect, useRef, useState } from "react";
import { api, company } from "../lib/api";
import {
  command,
  reconcileCompanyOperation,
  UnconfirmedCompanyOperation,
  type BusinessProfile,
  type BusinessSource,
} from "./api";

type Check = {
  id: string;
  sourceId: string;
  observedSourceId: string | null;
  status: string;
  checkedAt: string;
  notice: string | null;
};
type Review = {
  id: string;
  actorName: string;
  reviewedAt: string;
  review: { status: string; note: string; evidenceDigest: string };
};
type Field = { field: string; status: string; evidenceDigest: string; checks: Check[]; review: Review | null };
type Freshness = {
  blocked: boolean;
  fields: Field[];
  runs: { id: string; state: string; startedAt: string; completedAt: string | null }[];
};
const statusLabel: Record<string, string> = {
  unchecked: "Not checked",
  unchanged: "Source text unchanged",
  possible: "Possible change",
  unavailable: "Check unavailable",
  verified: "Owner verified",
  conflicting: "Conflicting",
  outdated: "Outdated",
};

export function BusinessFreshness({
  profile,
  owner,
  labels,
  sources,
  onCorrect,
  refresh,
}: {
  profile: BusinessProfile;
  owner: boolean;
  labels: Record<string, string>;
  sources: BusinessSource[];
  onCorrect: () => void;
  refresh: () => Promise<void>;
}) {
  const [data, setData] = useState<Freshness | null>(null),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  const ids = [...new Set(profile.fields.flatMap((f) => f.citations.map((c) => c.sourceId)))];
  const [selected, setSelected] = useState(ids.slice(0, 3));
  const key = `hirezero-freshness-v1:${company() || "local-company"}:${profile.id}`;
  const [pending, setPending] = useState(() => {
    try {
      return sessionStorage.getItem(key) || "";
    } catch {
      return "";
    }
  });
  const locked = useRef(false);
  const epoch = useRef(0);
  async function load() {
    const current = ++epoch.current;
    const saved = await api<Freshness>(`/company/business/freshness/${encodeURIComponent(profile.id)}`);
    if (current === epoch.current) setData(saved);
  }
  useEffect(() => {
    void load().catch((e) => setError((e as Error).message));
    return () => {
      epoch.current++;
    };
  }, [profile.id, profile.version]);
  function remember(id: string) {
    setPending(id);
    try {
      id ? sessionStorage.setItem(key, id) : sessionStorage.removeItem(key);
    } catch {
      /* The in-memory receipt still prevents a second request. */
    }
  }
  async function perform(action?: string, body?: Record<string, unknown>) {
    if (locked.current) return;
    locked.current = true;
    setBusy(true);
    setError("");
    let completed = false;
    try {
      if (pending) await reconcileCompanyOperation(pending);
      else if (action)
        await command(
          action,
          { id: profile.id, expectedVersion: profile.version, digest: profile.digest, ...body },
          remember,
        );
      remember("");
      completed = true;
    } catch (e) {
      if (!(e instanceof UnconfirmedCompanyOperation) && !pending) remember("");
      setError((e as Error).message);
    } finally {
      try {
        await load();
        await refresh();
      } catch {
        setError("Freshness status could not refresh. Check saved status before another request.");
      }
      locked.current = false;
      setBusy(false);
    }
    return completed;
  }
  const running = data?.runs[0]?.state === "running";
  useEffect(() => {
    if (!running) return;
    const timer = window.setTimeout(() => {
      void load().catch(() =>
        setError("The refresh may still be running. Check saved freshness; nothing was repeated."),
      );
    }, 1500);
    return () => window.clearTimeout(timer);
  }, [data, running]);
  const checks = [...new Map(data?.fields.flatMap((f) => f.checks).map((c) => [c.sourceId, c]) || []).values()];
  return (
    <section className="co-business-freshness" aria-label="Source freshness">
      <h3>Keep your business brief current</h3>
      <p>
        Check the pages behind your approved facts. Changes are evidence to review; the approved brief keeps its
        wording.
      </p>
      {data?.blocked && (
        <p className="co-error" role="alert">
          Work using this brief is paused because an owner marked a fact conflicting or outdated. Resolve the assessment
          or correct and replace the brief.
        </p>
      )}
      {error && (
        <p className="co-error" role="alert">
          {error}
        </p>
      )}
      {pending ? (
        <>
          <p role="status">
            {busy
              ? "Request in progress. Its saved receipt will confirm the outcome."
              : "A request needs confirmation. It will not be repeated."}
          </p>
          <button disabled={busy} onClick={() => void perform()}>
            Check freshness receipt
          </button>
        </>
      ) : running ? (
        <p role="status">Source refresh is running. Check saved status for the result.</p>
      ) : (
        owner &&
        ids.length > 0 && (
          <details>
            <summary>Check approved sources</summary>
            <p>
              Choose up to three pages for one bounded read of the confirmed website. Restricted pages are skipped. No
              model is called.
            </p>
            {ids.map((id) => (
              <label className="co-business-check" key={id}>
                <input
                  type="checkbox"
                  checked={selected.includes(id)}
                  disabled={busy || (!selected.includes(id) && selected.length >= 3)}
                  onChange={(e) => setSelected(e.target.checked ? [...selected, id] : selected.filter((s) => s !== id))}
                />
                <span>
                  {sources.find((s) => s.id === id)?.url || `Source ${ids.indexOf(id) + 1}`}
                  <br />
                  <small>
                    {profile.fields
                      .filter((f) => f.citations.some((c) => c.sourceId === id))
                      .map((f) => labels[f.key])
                      .join(", ")}
                  </small>
                </span>
              </label>
            ))}
            <button
              disabled={busy || !selected.length || !data}
              onClick={() =>
                void perform("business.refresh.start", { evidence: selected, decision: "refresh-approved-sources" })
              }
            >
              {busy ? "Checking sources…" : "Refresh selected sources"}
            </button>
          </details>
        )
      )}
      {ids.length === 0 && (
        <p>This brief uses owner knowledge rather than website citations. Correct it when the business changes.</p>
      )}
      {data?.runs[0] && (
        <p role="status">
          Latest refresh:{" "}
          {data.runs[0].state === "unknown"
            ? "interrupted; never replayed"
            : data.runs[0].state === "withheld"
              ? "withheld because the brief or authorization changed"
              : data.runs[0].state}
          . Requested {new Date(data.runs[0].startedAt).toLocaleString()}.
        </p>
      )}
      <button
        disabled={busy}
        onClick={() => {
          setError("");
          void load().catch((e) => setError((e as Error).message));
        }}
      >
        Check saved freshness
      </button>
      {checks.map((check) => (
        <SourceComparison key={check.id} check={check} profile={profile} />
      ))}
      {data && (
        <details className="co-business-fact-reviews" open={data.blocked || undefined}>
          <summary>Review facts and owner assessments</summary>
          <p>
            Unchanged text is not proof that a fact is true. Missing quotes may reflect clipping or rewritten text.
            Owner corrections remain authoritative.
          </p>
          {data.fields.map((field) => (
            <details key={field.field}>
              <summary>
                {labels[field.field]} · {statusLabel[field.status]}
              </summary>
              <p>{profile.fields.find((f) => f.key === field.field)?.value}</p>
              {field.review && (
                <p>
                  Last owner assessment: {statusLabel[field.review.review.status]} — {field.review.review.note}
                  <br />
                  <small>
                    {field.review.actorName}, {new Date(field.review.reviewedAt).toLocaleString()}
                    {field.review.review.evidenceDigest !== field.evidenceDigest &&
                      " · Source evidence has changed since this assessment."}
                  </small>
                </p>
              )}
              {owner && (
                <FactReview
                  key={`${field.field}:${field.evidenceDigest}:${field.review?.id}`}
                  field={field}
                  busy={busy || !!pending || !!running}
                  save={(input) => perform("business.fact.review", { body: JSON.stringify(input) })}
                />
              )}
            </details>
          ))}
        </details>
      )}
      {owner && (
        <button disabled={busy || !!pending} onClick={onCorrect}>
          Correct the brief
        </button>
      )}
    </section>
  );
}

function SourceComparison({ check, profile }: { check: Check; profile: BusinessProfile }) {
  const [pages, setPages] = useState<BusinessSource[] | null>(null),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  async function read() {
    setBusy(true);
    setError("");
    try {
      setPages(
        await Promise.all(
          [
            check.sourceId,
            ...(check.observedSourceId && check.observedSourceId !== check.sourceId ? [check.observedSourceId] : []),
          ].map((id) => api<BusinessSource>(`/company/business/sources/${encodeURIComponent(id)}`)),
        ),
      );
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  const citations = profile.fields.flatMap((f) => f.citations).filter((c) => c.sourceId === check.sourceId);
  const observed = check.observedSourceId ? pages?.find((p) => p.id === check.observedSourceId) : null;
  return (
    <details className="co-source-comparison">
      <summary>
        {check.status === "changed"
          ? "Source text changed — compare evidence"
          : check.status === "unchanged"
            ? "Source text unchanged"
            : "Source could not be checked"}{" "}
        · {new Date(check.checkedAt).toLocaleString()}
      </summary>
      {check.notice && <p>{check.notice}</p>}
      {error && (
        <p role="alert" className="co-error">
          {error}
        </p>
      )}
      {!pages && (
        <button disabled={busy} onClick={() => void read()}>
          {busy ? "Loading evidence…" : "Show source comparison"}
        </button>
      )}
      {pages && (
        <>
          {observed && (
            <ul>
              {[...new Set(citations.map((c) => c.quote))].map((quote) => (
                <li key={quote}>
                  <q>{quote}</q> —{" "}
                  {observed.text?.includes(quote)
                    ? "still present in the checked text"
                    : "not found in the checked text; review the full evidence"}
                </li>
              ))}
            </ul>
          )}
          <div className="co-source-columns">
            {pages.map((page, index) => (
              <section key={page.id}>
                <h4>{index === 0 ? "Approved citation snapshot" : "Newly checked snapshot"}</h4>
                <a href={page.url} target="_blank" rel="noreferrer">
                  {page.title || page.url}
                </a>
                <p>
                  Snapshot saved {new Date(page.retrievedAt).toLocaleString()}
                  {page.clipped ? " · Text was clipped" : ""}
                </p>
                <pre>{page.text}</pre>
              </section>
            ))}
          </div>
          <p>
            The check date is separate from the snapshot date. Identical content reuses its original saved snapshot.
          </p>
        </>
      )}
    </details>
  );
}

function FactReview({
  field,
  busy,
  save,
}: {
  field: Field;
  busy: boolean;
  save: (body: unknown) => Promise<boolean | undefined>;
}) {
  const [status, setStatus] = useState("possible"),
    [note, setNote] = useState(""),
    [quote, setQuote] = useState("");
  const sources = [...new Set(field.checks.map((c) => c.observedSourceId).filter((id): id is string => !!id))];
  const [source, setSource] = useState(sources[0] || "");
  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        void save({
          field: field.field,
          evidenceDigest: field.evidenceDigest,
          previousReviewId: field.review?.id || null,
          status,
          note,
          sourceId: status === "verified" ? source : null,
          quote: status === "verified" ? quote : null,
        }).then((ok) => {
          if (ok) {
            setNote("");
            setQuote("");
          }
        });
      }}
    >
      <fieldset disabled={busy}>
        <label>
          Assessment
          <select value={status} onChange={(e) => setStatus(e.target.value)}>
            <option value="possible">Possible change — needs confirmation</option>
            <option value="verified" disabled={!sources.length}>
              Verified against checked evidence
            </option>
            <option value="conflicting">Conflicting — pause use of this brief</option>
            <option value="outdated">Outdated — pause use of this brief</option>
          </select>
        </label>
        <label>
          Reason
          <textarea required maxLength={240} value={note} onChange={(e) => setNote(e.target.value)} />
        </label>
        {status === "verified" && (
          <>
            <label>
              Checked source
              <select value={source} onChange={(e) => setSource(e.target.value)}>
                {sources.map((id, i) => (
                  <option key={id} value={id}>
                    Checked source {i + 1}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Exact supporting quote
              <textarea required maxLength={600} value={quote} onChange={(e) => setQuote(e.target.value)} />
            </label>
            <small>
              Copy a supporting passage from the checked snapshot above. This assessment retains the approved wording
              and original classification.
            </small>
          </>
        )}
        {field.status === "conflicting" || field.status === "outdated" ? (
          <p>
            Saving a verified or possible assessment resolves this pause. Only do so when your reason explains why the
            brief can be used again.
          </p>
        ) : null}
        <button type="submit" disabled={!note.trim() || (status === "verified" && !quote.trim())}>
          Save fact assessment
        </button>
      </fieldset>
    </form>
  );
}
