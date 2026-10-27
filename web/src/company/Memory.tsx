import { memberStorage as sessionStorage } from "../lib/api";
import { useEffect, useMemo, useRef, useState } from "react";
import { api, company } from "../lib/api";
import { command, reconcileCompanyOperation, UnconfirmedCompanyOperation, type Snapshot, type Work } from "./api";
import "./memory.css";
import { MemoryAnalysis } from "./MemoryAnalysis";

type Definition = {
  kind: string;
  title: string;
  statement: string;
  rationale: string;
  departmentId?: string | null;
  employeeId?: string | null;
  reviewOn?: string | null;
  artifactId?: string | null;
  meetingId?: string | null;
  quote?: string | null;
  replacesId?: string | null;
  revisionSource?: { analysisId: string; version: number; finding: number; digest: string } | null;
};
type Origin = { artifactId: string; taskId: string; employeeId: string; version: number; quote: string };
type MeetingSource = { id: string; title: string; version: number; digest: string; text: string };
type MeetingOrigin = { meetingId: string; version: number; digest: string; quote: string };
type Record = {
  id: string;
  definition: Definition;
  origin: Origin | null;
  meetingOrigin?: MeetingOrigin | null;
  replacement: { id: string; version: number } | null;
  digest: string;
  state: string;
  version: number;
  approvedAt: string | null;
  deletionScope?: string | null;
};
type View = {
  memory: Record;
  usable: boolean;
  exclusion: string | null;
  analysisEligible: boolean;
  revisionCurrent: boolean;
};
type Draft = Definition & { audience: string; id?: string; version?: number };
type Source = {
  id: string;
  taskId: string;
  employeeId: string;
  employeeName: string;
  skill: string;
  version: number;
  text?: string | null;
};
type Retrieval = {
  notice: string;
  items: {
    memoryId: string;
    title: string;
    kind: string;
    version: number;
    statement: string;
    rationale: string;
    origin: Origin | null;
    meetingOrigin?: MeetingOrigin | null;
  }[];
};
type Pending = { id: string; fingerprint: string; action: string };
const kinds = [
  ["decision", "Decision"],
  ["goal", "Goal"],
  ["preference", "Preference"],
  ["project", "Project note"],
  ["feedback", "Owner feedback"],
  ["prior-work", "Prior work"],
];
const kindLabel = (kind: string) => kinds.find(([id]) => id === kind)?.[1] || kind;
const deletionNotice = (record: Record) =>
  record.deletionScope === "retired-source"
    ? "Saved memory deleted. Earlier task copies, reports, other drafts, audit metadata and backups remain."
    : "Unapproved memory deleted. Existing audit titles, source records and backups remain.";
const stateLabel = (state: string) =>
  ({
    draft: "Draft",
    approved: "Approved",
    needs_review: "Needs review",
    superseded: "Replaced",
    revoked: "Revoked",
    discarded: "Discarded",
  })[state] || state;
const nextReview = () => {
  const date = new Date();
  date.setUTCDate(date.getUTCDate() + 30);
  return date.toISOString().slice(0, 10);
};
const empty = (): Draft => ({
  kind: "decision",
  title: "",
  statement: "",
  rationale: "",
  audience: "company",
  reviewOn: nextReview(),
});

export function Memory({
  snapshot,
  role,
  initialId,
  initialWorkId,
  initialMeetingId,
  refresh,
  onOpenWork,
  onOpenMeeting,
}: {
  snapshot: Snapshot;
  role?: string;
  initialId?: string;
  initialWorkId?: string;
  initialMeetingId?: string;
  refresh: () => Promise<void>;
  onOpenWork: (work: Work) => void;
  onOpenMeeting: (id: string) => void;
}) {
  const prefix = `hirezero-memory-v1:${company() || "local-company"}:`;
  function remembered<T>(key: string): T | null {
    try {
      return JSON.parse(sessionStorage.getItem(prefix + key) || "null");
    } catch {
      return null;
    }
  }
  const [draft, setDraft] = useState<Draft>(() => {
    const saved = remembered<Draft>("draft");
    return saved && typeof saved.title === "string" && typeof saved.statement === "string" ? saved : empty();
  });
  const [editing, setEditing] = useState(() => !!draft.title || !!draft.statement),
    [pending, setPending] = useState<Pending | null>(() => remembered("receipt"));
  const [rows, setRows] = useState<View[]>([]),
    [sources, setSources] = useState<Source[]>([]),
    [source, setSource] = useState<Source | null>(null);
  const [sourceError, setSourceError] = useState(""),
    [sourceRefresh, setSourceRefresh] = useState(0);
  const [selected, setSelected] = useState(initialId || ""),
    [filter, setFilter] = useState("active"),
    [loaded, setLoaded] = useState(false);
  const [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [notice, setNotice] = useState(""),
    [reviewed, setReviewed] = useState(false),
    [deleteConfirmed, setDeleteConfirmed] = useState(false);
  const [query, setQuery] = useState(""),
    [employee, setEmployee] = useState(""),
    [retrieval, setRetrieval] = useState<Retrieval | null>(null),
    [previewing, setPreviewing] = useState(false);
  const locked = useRef(false),
    epoch = useRef(0),
    sourceEpoch = useRef(0),
    previewEpoch = useRef(0),
    draftRef = useRef(draft);
  draftRef.current = draft;
  const owner = !role || role === "owner",
    chosen = rows.find((row) => row.memory.id === selected),
    record = chosen?.memory;
  const analysisRecords = useMemo(
    () => rows.map((row) => ({ ...row.memory, analysisEligible: row.analysisEligible })),
    [rows],
  );
  const [feedbackWork, setFeedbackWork] = useState<Source | null>(null),
    [feedbackError, setFeedbackError] = useState(""),
    [feedbackDismissed, setFeedbackDismissed] = useState(false),
    [feedbackRefresh, setFeedbackRefresh] = useState(0);
  const unfinishedDraft =
    editing ||
    !!(
      draft.id ||
      draft.title ||
      draft.statement ||
      draft.rationale ||
      draft.artifactId ||
      draft.meetingId ||
      draft.replacesId
    );
  const [meetingEntry, setMeetingEntry] = useState<MeetingSource | null>(null),
    [meetingEntryError, setMeetingEntryError] = useState(""),
    [meetingDismissed, setMeetingDismissed] = useState(false),
    [citedMeeting, setCitedMeeting] = useState<MeetingSource | null>(null),
    [meetingSourceError, setMeetingSourceError] = useState("");
  useEffect(() => {
    let current = true;
    setMeetingEntry(null);
    setMeetingEntryError("");
    setMeetingDismissed(false);
    if (initialMeetingId && owner)
      void api<MeetingSource>(`/company/memory/meetings/${encodeURIComponent(initialMeetingId)}`)
        .then((value) => {
          if (current) setMeetingEntry(value);
        })
        .catch((error: Error) => {
          if (current) setMeetingEntryError(error.message);
        });
    return () => {
      current = false;
    };
  }, [initialMeetingId, owner, sourceRefresh]);
  useEffect(() => {
    let current = true;
    setCitedMeeting(null);
    setMeetingSourceError("");
    if (draft.meetingId)
      void api<MeetingSource>(`/company/memory/meetings/${encodeURIComponent(draft.meetingId)}`)
        .then((value) => {
          if (current) setCitedMeeting(value);
        })
        .catch((error: Error) => {
          if (current) setMeetingSourceError(error.message);
        });
    return () => {
      current = false;
    };
  }, [draft.meetingId, sourceRefresh]);
  useEffect(() => {
    let current = true;
    setFeedbackWork(null);
    setFeedbackError("");
    setFeedbackDismissed(false);
    if (initialWorkId && owner)
      void api<Source>(`/company/memory/work/${encodeURIComponent(initialWorkId)}`)
        .then((value) => {
          if (current) setFeedbackWork(value);
        })
        .catch((e) => {
          if (current) setFeedbackError((e as Error).message);
        });
    return () => {
      current = false;
    };
  }, [initialWorkId, owner, feedbackRefresh]);
  function persist(key: string, value: unknown) {
    try {
      value === null
        ? sessionStorage.removeItem(prefix + key)
        : sessionStorage.setItem(prefix + key, JSON.stringify(value));
    } catch {
      setNotice("This browser cannot retain unsaved edits. Save the draft before leaving.");
    }
  }
  function edit(next: Draft) {
    setDraft(next);
    draftRef.current = next;
    persist("draft", next);
  }
  function remember(value: Pending | null) {
    setPending(value);
    persist("receipt", value);
  }
  function captureFeedback() {
    if (!feedbackWork?.text || unfinishedDraft || busy || pending) return;
    edit({
      ...empty(),
      kind: "feedback",
      title: `Feedback on ${feedbackWork.employeeName}'s ${feedbackWork.skill.replaceAll("-", " ")}`.slice(0, 160),
      audience: `e:${feedbackWork.employeeId}`,
      artifactId: feedbackWork.id,
      quote: feedbackWork.text.slice(0, 600).trim(),
    });
    setEditing(true);
    setFeedbackDismissed(true);
    setNotice(
      "Write the guidance you want this employee to remember. Check the prefilled excerpt and audience before saving; approval is a separate step.",
    );
    requestAnimationFrame(() => {
      document.querySelector(".co-memory-editor")?.scrollIntoView({ block: "start", behavior: "smooth" });
      document.querySelector<HTMLTextAreaElement>(".co-memory-editor textarea")?.focus({ preventScroll: true });
    });
  }
  function captureMeetingDecision() {
    if (!meetingEntry || unfinishedDraft || busy || pending) return;
    edit({
      ...empty(),
      title: `Decision from ${meetingEntry.title}`.slice(0, 160),
      statement: meetingEntry.text.slice(0, 600).trim(),
      rationale: "Recorded in accepted meeting minutes; follow-up work requires separate approval.",
      meetingId: meetingEntry.id,
      quote: meetingEntry.text.slice(0, 600).trim(),
    });
    setEditing(true);
    setMeetingDismissed(true);
    setNotice(
      "Review the prefilled passage and write one reusable decision. Check its audience and review date; approval remains a separate step.",
    );
  }
  async function load() {
    const generation = ++epoch.current;
    try {
      const [memory, work] = await Promise.all([api<View[]>("/company/memory"), api<Source[]>("/company/memory/work")]);
      if (generation !== epoch.current) return;
      setRows(memory);
      setSources(work);
      setLoaded(true);
      previewEpoch.current++;
      setRetrieval(null);
      setPreviewing(false);
    } catch (e) {
      if (generation === epoch.current) throw e;
    }
  }
  useEffect(() => {
    void load().catch((e) => {
      setError((e as Error).message);
      setLoaded(true);
    });
    return () => {
      epoch.current++;
      sourceEpoch.current++;
      previewEpoch.current++;
    };
  }, []);
  useEffect(() => {
    if (initialId) setSelected(initialId);
  }, [initialId]);
  useEffect(() => {
    setReviewed(false);
    setDeleteConfirmed(false);
  }, [record?.id, record?.version, record?.digest]);
  useEffect(() => {
    previewEpoch.current++;
    setRetrieval(null);
    setPreviewing(false);
  }, [query, employee]);
  useEffect(() => {
    const generation = ++sourceEpoch.current;
    setSource(null);
    setSourceError("");
    if (draft.artifactId)
      void api<Source>(`/company/memory/work/${encodeURIComponent(draft.artifactId)}`)
        .then((value) => {
          if (generation === sourceEpoch.current) setSource(value);
        })
        .catch((e) => {
          if (generation === sourceEpoch.current) setSourceError((e as Error).message);
        });
  }, [draft.artifactId, sourceRefresh]);
  function choose(id: string) {
    setSelected(id);
    setReviewed(false);
    setDeleteConfirmed(false);
    setNotice("");
    requestAnimationFrame(() =>
      document.querySelector(".co-memory-review")?.scrollIntoView({ block: "start", behavior: "smooth" }),
    );
  }
  async function perform(action?: string, fields: { [key: string]: unknown } = {}) {
    if (locked.current) return;
    locked.current = true;
    epoch.current++;
    setBusy(true);
    setError("");
    setNotice("");
    const fingerprint = JSON.stringify(draftRef.current),
      saved = pending;
    try {
      const result = (
        saved
          ? await reconcileCompanyOperation(saved.id)
          : await command(action!, fields, (id) => remember({ id, fingerprint, action: action! }))
      ) as Record;
      remember(null);
      epoch.current++;
      setSelected(result.state === "deleted" ? "" : result.id);
      setReviewed(false);
      setDeleteConfirmed(false);
      if (result.state === "deleted") {
        setRows((current) => current.filter((row) => row.memory.id !== result.id));
        setNotice(deletionNotice(result));
      }
      if (
        ((saved?.action || action) === "memory.save" &&
          (saved?.fingerprint || fingerprint) === JSON.stringify(draftRef.current)) ||
        (result.state === "deleted" && draftRef.current.id === result.id)
      ) {
        edit(empty());
        persist("draft", null);
        setEditing(false);
      }
      await load();
      await refresh();
      setRetrieval(null);
      setNotice(
        result.state === "deleted"
          ? deletionNotice(result)
          : result.state === "draft"
            ? "Memory draft saved. Review its statement, audience, evidence and date before approval."
            : `Memory ${result.state}. Future task context follows this decision.`,
      );
      if ((saved?.action || action) === "memory.analysis.revise") {
        setFilter("active");
        requestAnimationFrame(() =>
          document.querySelector(".co-memory-review")?.scrollIntoView({ block: "start", behavior: "smooth" }),
        );
      }
    } catch (e) {
      if (!(e instanceof UnconfirmedCompanyOperation) && !saved) remember(null);
      setError((e as Error).message);
    } finally {
      locked.current = false;
      setBusy(false);
    }
  }
  function revise(value: Record) {
    const d = value.definition;
    edit({
      ...d,
      audience: d.employeeId ? `e:${d.employeeId}` : d.departmentId ? `d:${d.departmentId}` : "company",
      id: value.state === "draft" ? value.id : undefined,
      version: value.state === "draft" ? value.version : undefined,
      replacesId: value.state === "draft" ? d.replacesId : value.id,
      reviewOn: value.state === "draft" ? d.reviewOn : nextReview(),
      revisionSource: value.state === "draft" ? d.revisionSource : undefined,
    });
    setEditing(true);
    setNotice("");
    setReviewed(false);
    requestAnimationFrame(() =>
      document.querySelector(".co-memory-editor")?.scrollIntoView({ block: "start", behavior: "smooth" }),
    );
  }
  async function openSource(origin: Pick<Origin, "taskId">) {
    try {
      const result = await api<{ task: Work }>(`/company/tasks/${origin.taskId}`);
      await refresh();
      onOpenWork(result.task);
    } catch (e) {
      setError((e as Error).message);
    }
  }
  const audience = (d: Definition) =>
    d.employeeId
      ? snapshot.data.employees.find((e) => e.id === d.employeeId)?.name || "Employee"
      : d.departmentId
        ? snapshot.data.departments.find((department) => department.id === d.departmentId)?.name || "Department"
        : "Company-wide";
  const filtered = rows.filter((row) =>
    filter === "active"
      ? ["draft", "approved", "needs_review"].includes(row.memory.state)
      : filter === "current"
        ? row.usable
        : filter === "review"
          ? row.memory.state === "draft" ||
            row.memory.state === "needs_review" ||
            (row.memory.state === "approved" && !row.usable)
          : filter === "history"
            ? ["superseded", "revoked", "discarded"].includes(row.memory.state)
            : true,
  );
  return (
    <section className="co-memory" aria-label="Organizational memory">
      <header>
        <p className="co-eyebrow">Remember what matters</p>
        <h2>Decisions, goals and useful history</h2>
        <p>
          Save decisions your team should remember. Choose who can use them, add supporting evidence and set a review
          date when needed.
        </p>
      </header>
      {owner && initialMeetingId && !meetingDismissed && draft.meetingId !== initialMeetingId && (
        <section className="co-memory-feedback" aria-label="Decision from accepted minutes">
          <h3>What should the team remember?</h3>
          <p>Start an editable decision from accepted minutes. Saving a draft does not add it to employee context.</p>
          {meetingEntryError ? (
            <div role="alert">
              <p>{meetingEntryError}</p>
              <button disabled={busy} onClick={() => setSourceRefresh((value) => value + 1)}>
                Check accepted minutes again
              </button>
            </div>
          ) : !meetingEntry ? (
            <p role="status">Checking accepted minutes…</p>
          ) : (
            <>
              <p>
                {meetingEntry.title} · accepted version {meetingEntry.version}
              </p>
              {unfinishedDraft && (
                <p role="status">Finish your current memory draft first. Its unsaved text is preserved.</p>
              )}
              <div className="co-memory-toolbar">
                <button
                  className="co-primary"
                  disabled={busy || !!pending || unfinishedDraft}
                  onClick={captureMeetingDecision}
                >
                  Draft a decision from these minutes
                </button>
                <button disabled={busy} onClick={() => onOpenMeeting(meetingEntry.id)}>
                  Open accepted minutes
                </button>
              </div>
            </>
          )}
        </section>
      )}
      {owner &&
        initialWorkId &&
        !feedbackDismissed &&
        !(draft.kind === "feedback" && draft.artifactId === initialWorkId) && (
          <section className="co-memory-feedback" aria-label="Feedback from approved work">
            <h3>What should the next result do differently?</h3>
            <p>
              Capture reusable guidance from this result. It starts as a draft for this employee only; you review any
              wider audience before approval.
            </p>
            {feedbackError ? (
              <div role="alert">
                <p>{feedbackError}</p>
                <button disabled={busy} onClick={() => setFeedbackRefresh((value) => value + 1)}>
                  Check source result again
                </button>
              </div>
            ) : !feedbackWork ? (
              <p role="status">Checking the approved result…</p>
            ) : (
              <>
                <p>
                  {feedbackWork.employeeName} · {feedbackWork.skill.replaceAll("-", " ")} · approved result version{" "}
                  {feedbackWork.version}
                </p>
                {unfinishedDraft && (
                  <p role="status">Finish your current memory draft first. Its unsaved text is preserved.</p>
                )}
                <div className="co-memory-toolbar">
                  <button
                    className="co-primary"
                    disabled={busy || !!pending || unfinishedDraft || !feedbackWork.text}
                    onClick={captureFeedback}
                  >
                    Capture feedback from this result
                  </button>
                  <button disabled={busy} onClick={() => void openSource(feedbackWork)}>
                    Open source result
                  </button>
                </div>
              </>
            )}
          </section>
        )}
      {error && (
        <p role="alert" className="co-error">
          {error}
        </p>
      )}
      {notice && <p role="status">{notice}</p>}
      {pending && (
        <div role="status">
          <p>
            {busy
              ? "Saving this request…"
              : "Its outcome needs confirmation. Check the saved receipt before another change."}
          </p>
          <button disabled={busy} onClick={() => void perform()}>
            Check memory receipt
          </button>
        </div>
      )}
      <div className="co-memory-toolbar">
        {owner && !editing && (
          <button className="co-primary" disabled={busy || !!pending} onClick={() => setEditing(true)}>
            {draft.title || draft.statement ? "Continue unsaved memory" : "New memory"}
          </button>
        )}
        <button
          disabled={busy}
          onClick={() => {
            setError("");
            void load().catch((e) => setError((e as Error).message));
          }}
        >
          Refresh memory
        </button>
      </div>
      {editing && owner && (
        <form
          className="co-memory-editor"
          onSubmit={(e) => {
            e.preventDefault();
            const { id, version, audience, ...definition } = draft;
            void perform("memory.save", {
              id,
              expectedVersion: version || 0,
              body: JSON.stringify({
                ...definition,
                departmentId: audience.startsWith("d:") ? audience.slice(2) : null,
                employeeId: audience.startsWith("e:") ? audience.slice(2) : null,
              }),
            });
          }}
        >
          <h3>
            {draft.id ? "Edit memory draft" : draft.replacesId ? "Review a replacement" : "Capture a useful record"}
          </h3>
          <fieldset disabled={busy || !!pending}>
            {draft.revisionSource && (
              <p>
                This suggestion keeps its original audience and work citation. Edit the wording and review date, then
                save for separate approval.
              </p>
            )}
            <label>
              Record type
              <select
                disabled={!!draft.revisionSource}
                value={draft.kind}
                onChange={(e) => edit({ ...draft, kind: e.target.value })}
              >
                {kinds.map(([id, label]) => (
                  <option key={id} value={id}>
                    {label}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Title
              <input
                required
                maxLength={160}
                value={draft.title}
                onChange={(e) => edit({ ...draft, title: e.target.value })}
              />
            </label>
            <label>
              Statement
              <textarea
                required
                maxLength={1200}
                rows={4}
                value={draft.statement}
                onChange={(e) => edit({ ...draft, statement: e.target.value })}
              />
            </label>
            {draft.kind === "feedback" && (
              <p>
                Write an instruction for future work, such as what to keep, change or avoid. Saving alone does not
                change employee context.
              </p>
            )}
            <label>
              Why this matters
              <textarea
                maxLength={600}
                rows={3}
                value={draft.rationale}
                onChange={(e) => edit({ ...draft, rationale: e.target.value })}
              />
            </label>
            <label>
              Employee audience
              <select
                disabled={!!draft.revisionSource}
                value={draft.audience}
                onChange={(e) => edit({ ...draft, audience: e.target.value })}
              >
                <option value="company">Company-wide</option>
                {snapshot.data.departments.map((d) => (
                  <option key={d.id} value={`d:${d.id}`}>
                    Department: {d.name}
                  </option>
                ))}
                {snapshot.data.employees
                  .filter((e) => e.state === "active")
                  .map((e) => (
                    <option key={e.id} value={`e:${e.id}`}>
                      Employee: {e.name}
                    </option>
                  ))}
              </select>
            </label>
            <label>
              Review on (UTC)
              <input
                type="date"
                value={draft.reviewOn || ""}
                onChange={(e) => edit({ ...draft, reviewOn: e.target.value || null })}
              />
            </label>
            <small>
              Employees stop using this record at the start of that UTC date. Leave blank for no automatic expiry.
            </small>
            <label>
              Cite approved work
              <select
                disabled={!!draft.revisionSource}
                value={draft.artifactId || ""}
                onChange={(e) => edit({ ...draft, artifactId: e.target.value || null, meetingId: null, quote: null })}
              >
                <option value="">No work citation</option>
                {draft.artifactId && !sources.some((item) => item.id === draft.artifactId) && (
                  <option value={draft.artifactId} disabled>
                    {source?.id === draft.artifactId
                      ? `${source.employeeName} · selected result`
                      : sourceError
                        ? "Selected result (unavailable)"
                        : "Checking selected result…"}
                  </option>
                )}
                {sources.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.employeeName} · {s.skill} · {s.id.slice(0, 6)}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Cite accepted minutes
              <select
                disabled={!!draft.revisionSource}
                value={draft.meetingId || ""}
                onChange={(e) => edit({ ...draft, meetingId: e.target.value || null, artifactId: null, quote: null })}
              >
                <option value="">No minutes citation</option>
                {draft.meetingId &&
                  !snapshot.data.meetings.some((m) => m.id === draft.meetingId && m.state === "closed") && (
                    <option value={draft.meetingId}>Selected minutes (check availability)</option>
                  )}
                {snapshot.data.meetings
                  .filter((m) => m.state === "closed")
                  .map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.title}
                    </option>
                  ))}
              </select>
            </label>
            {draft.kind === "prior-work" && !draft.artifactId && !draft.meetingId && (
              <p>Prior-work records need an approved result or accepted minutes citation.</p>
            )}
            {(draft.artifactId || draft.meetingId) && (
              <>
                {(draft.meetingId ? meetingSourceError : sourceError) && (
                  <div role="alert">
                    <p>{draft.meetingId ? meetingSourceError : sourceError}</p>
                    <button type="button" onClick={() => setSourceRefresh((value) => value + 1)}>
                      {draft.meetingId ? "Check cited minutes again" : "Check cited result again"}
                    </button>
                  </div>
                )}
                <details>
                  <summary>{draft.meetingId ? "Read the accepted minutes" : "Read the approved result"}</summary>
                  <pre>
                    {(draft.meetingId ? citedMeeting?.text : source?.text) ||
                      ((draft.meetingId ? meetingSourceError : sourceError)
                        ? "This citation could not be verified. Your draft is preserved."
                        : "Loading the approved source…")}
                  </pre>
                </details>
                <label>
                  Exact supporting quote
                  <textarea
                    required
                    disabled={!!draft.revisionSource}
                    maxLength={600}
                    rows={3}
                    value={draft.quote || ""}
                    onChange={(e) => edit({ ...draft, quote: e.target.value })}
                  />
                </label>
                <p>
                  Approving shares this quote with the selected employee audience. Other private conversation content is
                  excluded.
                </p>
                {draft.kind === "feedback" && (
                  <p>
                    The opening excerpt may be prefilled. Replace it with the passage your feedback refers to when
                    needed.
                  </p>
                )}
              </>
            )}
            {draft.replacesId && (
              <p>Approval will replace the earlier record in one step. Its history remains available.</p>
            )}
            <div className="co-memory-toolbar">
              <button
                className="co-primary"
                type="submit"
                disabled={
                  (draft.kind === "prior-work" && !draft.artifactId && !draft.meetingId) ||
                  (!!draft.artifactId && (source?.id !== draft.artifactId || !!sourceError)) ||
                  (!!draft.meetingId && (citedMeeting?.id !== draft.meetingId || !!meetingSourceError))
                }
              >
                Save memory draft
              </button>
              <button type="button" onClick={() => setEditing(false)}>
                Keep draft for later
              </button>
              <button
                type="button"
                onClick={() => {
                  edit(empty());
                  setEditing(false);
                  setNotice("Unsaved edits discarded. Saved memory is unchanged.");
                }}
              >
                Discard unsaved edits
              </button>
            </div>
          </fieldset>
        </form>
      )}
      <div className="co-memory-toolbar">
        <label>
          Show
          <select value={filter} onChange={(e) => setFilter(e.target.value)}>
            <option value="active">Active records</option>
            <option value="all">All records</option>
            <option value="current">Current and approved</option>
            <option value="review">Needs review</option>
            <option value="history">Retired history</option>
          </select>
        </label>
        <span>{rows.length} / 200 retained records</span>
      </div>
      {!loaded ? (
        <p role="status">Loading saved memory…</p>
      ) : (
        !filtered.length && <p>No records in this view. Start with one decision you would otherwise explain again.</p>
      )}
      {loaded && selected && !record && (
        <p role="status">This memory record is unavailable in this company. Choose a record below.</p>
      )}
      <ul className="co-memory-list">
        {filtered.map(({ memory, usable }) => (
          <li key={memory.id}>
            <button aria-pressed={selected === memory.id} disabled={busy} onClick={() => choose(memory.id)}>
              <strong>{memory.definition.title}</strong>
              <span>
                {kindLabel(memory.definition.kind)} ·{" "}
                {usable ? "Current" : memory.state === "approved" ? "Needs review" : stateLabel(memory.state)} ·{" "}
                {audience(memory.definition)}
              </span>
            </button>
          </li>
        ))}
      </ul>
      {record && chosen && (
        <article className="co-memory-review" aria-label="Memory review">
          <h3>{record.definition.title}</h3>
          <p>
            {kindLabel(record.definition.kind)} · version {record.version} · {audience(record.definition)} ·{" "}
            {stateLabel(record.state)}
          </p>
          {chosen.exclusion && record.state !== "draft" && (
            <p role="status" className="co-error">
              {chosen.exclusion}
            </p>
          )}
          <h4>Statement</h4>
          {record.definition.revisionSource && (
            <p role="status">
              {record.state === "draft"
                ? chosen.revisionCurrent
                  ? "Suggested by a memory report. Check the wording and earlier record before approving."
                  : "Supporting memory changed since this suggestion. Discard this draft and check current records before creating another revision."
                : "Started from a memory report and reviewed by the owner."}
            </p>
          )}
          <p className="co-memory-statement">{record.definition.statement}</p>
          {record.definition.rationale && (
            <>
              <h4>Why this matters</h4>
              <p>{record.definition.rationale}</p>
            </>
          )}
          <p>
            {record.definition.reviewOn
              ? `Review by ${record.definition.reviewOn} at 00:00 UTC.`
              : "No automatic expiry."}
            {record.approvedAt && ` Approved ${new Date(record.approvedAt).toLocaleString()}.`}
          </p>
          {record.origin && (
            <div className="co-memory-origin">
              <h4>Cited work · version {record.origin.version}</h4>
              <blockquote>{record.origin.quote}</blockquote>
              <button onClick={() => void openSource(record.origin!)}>Open cited result</button>
            </div>
          )}
          {record.meetingOrigin && (
            <div className="co-memory-origin">
              <h4>Accepted minutes · version {record.meetingOrigin.version}</h4>
              <blockquote>{record.meetingOrigin.quote}</blockquote>
              <button onClick={() => onOpenMeeting(record.meetingOrigin!.meetingId)}>Open cited minutes</button>
            </div>
          )}
          {record.replacement && (
            <div className="co-memory-replacement">
              <h4>{record.state === "draft" ? "Earlier record being replaced" : "Earlier record replaced"}</h4>
              <p>
                {rows.find((r) => r.memory.id === record.replacement?.id)?.memory.definition.statement ||
                  "Refresh to inspect the earlier record."}
              </p>
              <p>
                Earlier version {record.replacement.version}
                {rows.find((r) => r.memory.id === record.replacement?.id)?.memory.definition &&
                  ` · ${audience(rows.find((r) => r.memory.id === record.replacement?.id)!.memory.definition)}`}
                .
                {record.state === "draft" && " Approving this replacement retires the earlier record in the same step."}
              </p>
            </div>
          )}
          {owner && (
            <div className="co-memory-controls">
              {record.state === "draft" && (
                <>
                  <label className="co-memory-check">
                    <input
                      type="checkbox"
                      checked={reviewed}
                      disabled={busy || !!pending}
                      onChange={(e) => setReviewed(e.target.checked)}
                    />{" "}
                    I reviewed this statement, evidence, audience and date
                    {record.replacement ? ", including the earlier record it replaces" : ""}.
                  </label>
                  <button
                    className="co-primary"
                    disabled={!reviewed || busy || !!pending || !chosen.revisionCurrent}
                    onClick={() =>
                      void perform("memory.review", {
                        id: record.id,
                        expectedVersion: record.version,
                        digest: record.digest,
                        decision: record.replacement ? "approve-and-replace" : "approve",
                      })
                    }
                  >
                    {record.replacement ? "Approve and replace earlier memory" : "Approve memory"}
                  </button>
                </>
              )}
              {["draft", "approved", "needs_review"].includes(record.state) && (
                <button disabled={busy || !!pending || unfinishedDraft} onClick={() => revise(record)}>
                  {record.state === "draft" ? "Edit saved memory draft" : "Revise or renew memory"}
                </button>
              )}
              {["draft", "approved", "needs_review"].includes(record.state) && (
                <button
                  disabled={busy || !!pending}
                  onClick={() =>
                    void perform("memory.review", {
                      id: record.id,
                      expectedVersion: record.version,
                      digest: record.digest,
                      decision: record.state === "draft" ? "discard" : "revoke",
                    })
                  }
                >
                  {record.state === "draft" ? "Discard draft" : "Revoke memory"}
                </button>
              )}
              {["draft", "discarded"].includes(record.state) && !record.approvedAt && (
                <details className="co-memory-delete" key={`${record.id}:${record.version}`}>
                  <summary>Delete this unapproved memory</summary>
                  <p>
                    Removes this draft’s saved statement, rationale, title, citations and command recovery copies.
                    Unsaved edits to this same draft in this browser are also cleared. It has never been approved for
                    employee use. Discarding alone keeps the saved text.
                  </p>
                  <p>
                    Existing audit entries may retain an earlier title. Cited results, analysis reports, other drafts,
                    other browser copies, exports, backups and database recovery files remain. This does not erase the
                    whole company’s history.
                  </p>
                  <label className="co-memory-check">
                    <input
                      type="checkbox"
                      checked={deleteConfirmed}
                      disabled={busy || !!pending}
                      onChange={(e) => setDeleteConfirmed(e.target.checked)}
                    />
                    Delete this never-approved draft. I understand which copies remain.
                  </label>
                  <button
                    disabled={!deleteConfirmed || busy || !!pending}
                    onClick={() =>
                      void perform("memory.delete", {
                        id: record.id,
                        expectedVersion: record.version,
                        digest: record.digest,
                        decision: "delete-unapproved",
                      })
                    }
                  >
                    Permanently delete unapproved memory
                  </button>
                </details>
              )}
              {["revoked", "superseded"].includes(record.state) && !!record.approvedAt && (
                <details className="co-memory-source-delete" key={`${record.id}:${record.version}`}>
                  <summary>Delete this saved memory</summary>
                  <p>
                    This memory is already {record.state === "superseded" ? "replaced" : "revoked"} and excluded from
                    new task context. Delete its saved title, statement, rationale, citations and command recovery
                    copies. This cannot be undone here.
                  </p>
                  <p>
                    Earlier task copies, analysis reports, cited results or minutes, replacement drafts, other browser
                    copies, audit metadata, exports, backups and database recovery files remain. Approved replacement
                    memory is unchanged. This does not erase the company’s full history.
                  </p>
                  <label className="co-memory-check">
                    <input
                      type="checkbox"
                      checked={deleteConfirmed}
                      disabled={busy || !!pending}
                      onChange={(e) => setDeleteConfirmed(e.target.checked)}
                    />
                    Delete this saved record. I understand which copies remain.
                  </label>
                  <button
                    disabled={!deleteConfirmed || busy || !!pending}
                    onClick={() =>
                      void perform("memory.source.delete", {
                        id: record.id,
                        expectedVersion: record.version,
                        digest: record.digest,
                        decision: "delete-retired-source",
                      })
                    }
                  >
                    Permanently delete saved memory
                  </button>
                </details>
              )}
            </div>
          )}
        </article>
      )}
      {owner && loaded && (
        <MemoryAnalysis
          snapshot={snapshot}
          records={analysisRecords}
          onReview={choose}
          onRevision={(fields) => void perform("memory.analysis.revise", fields)}
          revisionUnavailable={busy || !!pending || unfinishedDraft}
          refresh={async () => {
            await load();
            await refresh();
          }}
        />
      )}
      <section className="co-memory-preview">
        <h3>Preview what an employee would remember</h3>
        <p>
          Keyword retrieval only; no model request. Future tasks include at most three current approved records within
          their context allowance.
        </p>
        <form
          onSubmit={async (e) => {
            e.preventDefault();
            setError("");
            const generation = ++previewEpoch.current;
            setPreviewing(true);
            try {
              const result = await api<Retrieval>("/company/memory/retrieve", "POST", {
                query,
                employeeId: employee || null,
                companyOnly: !employee,
              });
              if (generation === previewEpoch.current) setRetrieval(result);
            } catch (e) {
              if (generation === previewEpoch.current) setError((e as Error).message);
            } finally {
              if (generation === previewEpoch.current) setPreviewing(false);
            }
          }}
        >
          <label>
            Task or topic
            <input required maxLength={4000} value={query} onChange={(e) => setQuery(e.target.value)} />
          </label>
          <label>
            Retrieval audience
            <select value={employee} onChange={(e) => setEmployee(e.target.value)}>
              <option value="">Company conversation</option>
              {snapshot.data.employees
                .filter((e) => e.state === "active")
                .map((e) => (
                  <option key={e.id} value={e.id}>
                    {e.name}
                  </option>
                ))}
            </select>
          </label>
          <button type="submit" disabled={previewing}>
            {previewing ? "Finding relevant memory…" : "Preview memory"}
          </button>
        </form>
        {retrieval && (
          <div className="co-memory-matches">
            <p role="status">{retrieval.notice}</p>
            {retrieval.items.map((item) => (
              <article key={item.memoryId}>
                <h4>
                  {item.title} · version {item.version}
                </h4>
                <p>{item.statement}</p>
                {item.origin && <blockquote>{item.origin.quote}</blockquote>}
                {item.meetingOrigin && <blockquote>{item.meetingOrigin.quote}</blockquote>}
                <button onClick={() => choose(item.memoryId)}>Review this memory</button>
              </article>
            ))}
          </div>
        )}
      </section>
    </section>
  );
}
