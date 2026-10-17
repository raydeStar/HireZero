import { useCallback, useEffect, useState } from "react";
import { ApiError, api, when } from "./api";

type Hit = { path: string; line: number; score: number; snippet: string; source: string; flags: string[] };
type Suggestion = {
  id: string;
  sourceKind: string;
  sourceTitle: string;
  text: string;
  reason: string;
  state: string;
  createdAt: string;
};
type Overview = {
  longTerm: string;
  longTermTruncated: boolean;
  notes: { path: string; bytes: number; updatedAt: string }[];
  dreams: string | null;
  store: string;
};

/** Facts the agent proposed from the inbox and finished requests. Nothing reaches memory until accepted. */
function Suggestions({ onChanged, fail }: { onChanged: () => void; fail: (cause: unknown) => void }) {
  const [data, setData] = useState<{ pending: Suggestion[]; recent: Suggestion[] } | null>(null),
    [edits, setEdits] = useState<Record<string, string>>({}),
    [busy, setBusy] = useState("");
  const load = useCallback(async () => {
    try {
      setData(await api<{ pending: Suggestion[]; recent: Suggestion[] }>("/memory/suggestions"));
    } catch (cause) {
      fail(cause);
    }
  }, [fail]);
  useEffect(() => {
    void load();
  }, [load]);
  async function decide(item: Suggestion, action: "accept" | "dismiss") {
    setBusy(item.id);
    try {
      await api(
        "/memory/suggestions/" + item.id + "/" + action,
        "POST",
        action === "accept" ? { text: edits[item.id] ?? null } : {},
      );
      await load();
      if (action === "accept") onChanged();
    } catch (cause) {
      fail(cause);
    } finally {
      setBusy("");
    }
  }
  return (
    <section className="fe-section fe-stack">
      <h3>Suggested to remember {!!data?.pending.length && <span className="fe-count">{data.pending.length}</span>}</h3>
      <p className="fe-muted">
        While on shift, the agent reads new inbox items and finished requests and suggests durable facts. Edit before
        accepting if needed. Only accepted facts are saved.
      </p>
      {!data ? (
        <p role="status">Loading…</p>
      ) : data.pending.length ? (
        <ul className="dev-memory-suggestions">
          {data.pending.map((item) => (
            <li key={item.id} className="fe-stack">
              <textarea
                aria-label="Suggested fact"
                rows={2}
                maxLength={2000}
                value={edits[item.id] ?? item.text}
                onChange={(event) => setEdits((current) => ({ ...current, [item.id]: event.target.value }))}
              />
              <small className="fe-muted">
                {item.reason && item.reason + " · "}From {item.sourceKind === "intake" ? "inbox" : "request"}:{" "}
                {item.sourceTitle}
              </small>
              <div className="dev-shift-actions">
                <button className="primary" disabled={busy === item.id} onClick={() => void decide(item, "accept")}>
                  Remember
                </button>
                <button className="fe-ghost" disabled={busy === item.id} onClick={() => void decide(item, "dismiss")}>
                  Dismiss
                </button>
              </div>
            </li>
          ))}
        </ul>
      ) : (
        <p className="fe-muted">No suggestions waiting.</p>
      )}
      {!!data?.recent.length && (
        <details>
          <summary className="fe-muted">Recently decided</summary>
          <ul className="dev-digest-files">
            {data.recent.map((item) => (
              <li key={item.id}>
                {item.state === "accepted" ? "✓" : "✕"} {item.text}
              </li>
            ))}
          </ul>
        </details>
      )}
    </section>
  );
}

/** Work tab: what the agent remembers. Notes live in OpenClaw's memory; dreaming distills them into long-term memory overnight. */
export function MemoryView({ onForbidden }: { onForbidden: () => void }) {
  const [overview, setOverview] = useState<Overview | null>(null),
    [query, setQuery] = useState(""),
    [hits, setHits] = useState<Hit[] | null>(null),
    [note, setNote] = useState("");
  const [busy, setBusy] = useState(""),
    [error, setError] = useState(""),
    [notice, setNotice] = useState("");
  const fail = useCallback(
    (cause: unknown) => {
      if (cause instanceof ApiError && cause.status === 403) onForbidden();
      else setError((cause as Error).message);
    },
    [onForbidden],
  );
  const load = useCallback(async () => {
    try {
      setOverview(await api<Overview>("/memory"));
    } catch (cause) {
      fail(cause);
    }
  }, [fail]);
  useEffect(() => {
    void load();
  }, [load]);
  async function search() {
    setBusy("search");
    setError("");
    try {
      setHits((await api<{ results: Hit[] }>("/memory/search?q=" + encodeURIComponent(query))).results);
    } catch (cause) {
      fail(cause);
    } finally {
      setBusy("");
    }
  }
  async function remember() {
    setBusy("note");
    setError("");
    setNotice("");
    try {
      await api("/memory/notes", "POST", { text: note });
      setNote("");
      setNotice("Saved to today’s notes. It’s searchable in a few seconds.");
      await load();
    } catch (cause) {
      fail(cause);
    } finally {
      setBusy("");
    }
  }
  return (
    <div className="fe-stack dev-memory">
      <p className="fe-muted">
        The agent's memory lives in OpenClaw on this computer. Notes are searchable right away. Each night OpenClaw's
        dreaming pass promotes what keeps coming up into long-term memory, which every conversation starts with. Search
        here is keyword-only, so notes never leave this computer to be indexed.
      </p>
      {error && (
        <p className="fe-alert" role="alert">
          {error}
        </p>
      )}
      <Suggestions onChanged={() => void load()} fail={fail} />
      <section className="fe-section fe-stack">
        <h3>Search memory</h3>
        <form
          className="dev-shift-actions"
          onSubmit={(event) => {
            event.preventDefault();
            void search();
          }}
        >
          <input
            type="search"
            aria-label="Search memory"
            value={query}
            maxLength={200}
            placeholder="squash merges, Example, deploy steps…"
            onChange={(event) => setQuery(event.target.value)}
          />
          <button className="primary" disabled={busy === "search" || query.trim().length < 2}>
            {busy === "search" ? "Searching…" : "Search"}
          </button>
        </form>
        {hits &&
          (hits.length ? (
            <ol className="dev-memory-hits">
              {hits.map((hit, index) => (
                <li key={index}>
                  <small className="fe-muted">
                    <code>{hit.path}</code>:{hit.line} · {hit.source}
                    {hit.flags.includes("possible-injection") && (
                      <span className="dev-flag" title="This text reads like instructions; it is shown as data only.">
                        {" "}
                        flagged
                      </span>
                    )}
                  </small>
                  <pre>{hit.snippet}</pre>
                </li>
              ))}
            </ol>
          ) : (
            <p className="fe-muted">Nothing found.</p>
          ))}
      </section>
      <section className="fe-section fe-stack">
        <h3>Remember something</h3>
        <textarea
          aria-label="Note to remember"
          rows={2}
          maxLength={2000}
          value={note}
          placeholder="A fact or preference, e.g. “Dependabot PRs on ExampleWidgetProject can be squash-merged once CI passes.”"
          onChange={(event) => setNote(event.target.value)}
        />
        <div className="dev-shift-actions">
          <button className="primary" disabled={busy === "note" || !note.trim()} onClick={() => void remember()}>
            {busy === "note" ? "Saving…" : "Remember"}
          </button>
          {notice && (
            <span className="fe-muted" role="status">
              {notice}
            </span>
          )}
        </div>
      </section>
      <section className="fe-section fe-stack">
        <h3>Long-term memory</h3>
        {!overview ? (
          <p role="status">Loading…</p>
        ) : overview.longTerm ? (
          <>
            <pre className="dev-memory-long">{overview.longTerm}</pre>
            {overview.longTermTruncated && <p className="fe-muted">Showing the start only.</p>}
          </>
        ) : (
          <p className="fe-muted">
            Nothing promoted yet. Dreaming runs nightly at 3:00 and promotes notes that keep proving useful.
          </p>
        )}
        {!!overview?.notes.length && (
          <details>
            <summary className="fe-muted">Recent notes ({overview.notes.length})</summary>
            <ul className="dev-digest-files">
              {overview.notes.map((file) => (
                <li key={file.path}>
                  <code>{file.path}</code> <span className="fe-muted">· {when(file.updatedAt)}</span>
                </li>
              ))}
            </ul>
          </details>
        )}
        {overview?.dreams && (
          <details>
            <summary className="fe-muted">Latest dream diary</summary>
            <pre className="dev-memory-long">{overview.dreams}</pre>
          </details>
        )}
      </section>
    </div>
  );
}
