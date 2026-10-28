import { memberStorage as sessionStorage } from "../lib/api";
import { useRef, useState } from "react";
import { company } from "../lib/api";
import {
  command,
  reconcileCompanyOperation,
  UnconfirmedCompanyOperation,
  type Snapshot,
  type BusinessIdentity,
} from "./api";

export function BusinessLookup({
  snapshot,
  identity,
  kind,
  refresh,
  choose,
}: {
  snapshot: Snapshot;
  identity: BusinessIdentity;
  kind: "search" | "discovery";
  refresh: () => Promise<void>;
  choose?: (url: string) => void;
}) {
  const key = `hirezero-business-lookup-v1:${company() || "local-company"}:${kind}:${identity.version}`;
  const [pending, setPending] = useState(() => {
    try {
      return sessionStorage.getItem(key) || "";
    } catch {
      return "";
    }
  });
  const [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [consent, setConsent] = useState(false);
  const locked = useRef(false);
  const runs = (snapshot.data.businessLookups || []).filter(
    (r) => r.kind === kind && r.businessVersion === identity.version,
  );
  const latest = runs.at(-1);
  const searchUnavailable = kind === "search" && snapshot.capabilities.businessSearch === false;
  function remember(id: string) {
    setPending(id);
    try {
      id ? sessionStorage.setItem(key, id) : sessionStorage.removeItem(key);
    } catch {
      /* Keep this attempt in memory. */
    }
  }
  async function run() {
    if (locked.current) return;
    locked.current = true;
    setBusy(true);
    setError("");
    try {
      if (pending) await reconcileCompanyOperation(pending);
      else
        await command(
          "business.lookup.start",
          { body: kind, expectedVersion: identity.version, digest: identity.digest, decision: "authorize-one-lookup" },
          remember,
        );
      remember("");
      setConsent(false);
    } catch (e) {
      if (!(e instanceof UnconfirmedCompanyOperation) && !pending) remember("");
      setError((e as Error).message);
    } finally {
      await refresh().catch(() =>
        setError("Saved status could not refresh. Check lookup status before starting another request."),
      );
      locked.current = false;
      setBusy(false);
    }
  }
  return (
    <section
      className="co-business-lookup"
      aria-label={kind === "search" ? "Find business websites" : "Discover website pages"}
    >
      <h4>{kind === "search" ? "Need help finding the website?" : "Let HireZero find the useful pages"}</h4>
      <p>
        {kind === "search"
          ? "Search uses the saved business name, location and industry. Results are possible matches; choose and confirm the correct business before importing anything."
          : "Read the confirmed website and up to two relevant About, services, products, pricing or blog pages. Each source keeps its address and retrieval date. Restricted pages are skipped."}
      </p>
      {kind === "search" && (
        <p>
          <b>Search:</b> {[identity.name, identity.location, identity.industry].filter(Boolean).join(" ")}
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
            {busy ? "Looking up website evidence…" : "This request needs confirmation. It will not be repeated."}
          </p>
          <button disabled={busy} onClick={() => void run()}>
            Check lookup receipt
          </button>
        </>
      ) : searchUnavailable ? (
        <p>
          Website search is not connected. Enter the website yourself, or ask the workspace operator to configure the
          search service.
        </p>
      ) : latest?.state === "running" ? (
        <p role="status">Lookup is running. Refresh its saved status to see the result.</p>
      ) : (
        <>
          {kind === "search" && (
            <label className="co-business-check">
              <input type="checkbox" checked={consent} disabled={busy} onChange={(e) => setConsent(e.target.checked)} />
              <span>
                I authorize one request to the configured search service. Its subscription may charge for this request.
              </span>
            </label>
          )}
          <button
            className={kind === "discovery" && !latest ? "co-primary" : undefined}
            type="button"
            disabled={busy || (kind === "search" && !consent)}
            onClick={() => void run()}
          >
            {busy ? "Looking up…" : kind === "search" ? "Find possible websites" : "Discover website pages"}
          </button>
        </>
      )}
      {latest && (
        <div className="co-business-lookup-result" role="status">
          <p>
            {latest.state === "completed"
              ? "Lookup complete."
              : latest.state === "partial"
                ? "Lookup finished with limitations."
                : latest.state === "withheld"
                  ? "Results withheld because the business or authorization changed."
                  : latest.state === "unknown"
                    ? "The outcome is uncertain. This request was not replayed."
                    : "Request recorded."}
            {!!latest.sourceIds?.length &&
              ` ${latest.sourceIds.length} saved source${latest.sourceIds.length === 1 ? "" : "s"} available below.`}
          </p>
          {latest.notices?.map((notice, i) => (
            <p key={i}>{notice}</p>
          ))}
          {!!latest.candidates?.length && (
            <ul className="co-business-candidates">
              {latest.candidates.map((candidate) => (
                <li key={candidate.url}>
                  <h4>{candidate.title || "Possible business website"}</h4>
                  <p>{candidate.description}</p>
                  <a href={candidate.url} target="_blank" rel="noreferrer">
                    {candidate.url}
                  </a>
                  <button
                    aria-label={`Use ${candidate.title || candidate.url} website for review`}
                    disabled={busy}
                    onClick={() => choose?.(candidate.url)}
                  >
                    Use this website for review
                  </button>
                </li>
              ))}
            </ul>
          )}
          <small>
            Requested {new Date(latest.startedAt).toLocaleString()}.{" "}
            {kind === "search"
              ? "Search suggestions are not company facts."
              : "Imported pages still need profile review."}
          </small>
        </div>
      )}
      {(latest || error) && (
        <button
          type="button"
          disabled={busy}
          onClick={() =>
            void refresh().catch(() => setError("Lookup status is still unavailable. Nothing was repeated."))
          }
        >
          Refresh lookup status
        </button>
      )}
    </section>
  );
}
