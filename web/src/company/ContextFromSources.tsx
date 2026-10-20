import { useEffect, useState } from "react";
import { api } from "../lib/api";
import { command, type Snapshot, type ThreadDetail } from "./api";
import { MeasuredJob } from "./MeasuredJob";
import { humanize } from "./labels";
import { parseDraft } from "./contextDraft";

type SourceRead = {
  source: string;
  read: boolean;
  title: string;
  characters: number;
  clipped: boolean;
  error?: string;
};

// Fill from sources (docs/adr/0041-context-drafted-from-sources.md): HireZero reads the owner's pages and repositories
// without a model, the employee drafts every field in one turn the owner authorizes, and the draft only fills the form.
// Nothing becomes knowledge until the owner edits, saves and approves it.
export function ContextFromSources({
  snapshot,
  employeeId,
  employeeName,
  fields,
  scope,
  refresh,
  onFill,
}: {
  snapshot: Snapshot;
  employeeId: string;
  employeeName: string;
  fields: string[];
  // "company" drafts the company-wide facts through this employee instead of its department context.
  scope?: "company";
  refresh: () => Promise<void>;
  onFill: (values: Record<string, string>) => void;
}) {
  const cents = snapshot.capabilities.allowanceUnit === "usd-cents";
  const [sources, setSources] = useState("");
  const [allowance, setAllowance] = useState("1");
  const [reading, setReading] = useState(false);
  const [error, setError] = useState("");
  const [results, setResults] = useState<SourceRead[]>([]);
  const [taskId, setTaskId] = useState("");
  const [answer, setAnswer] = useState("");
  const [filled, setFilled] = useState(false);
  const task = snapshot.data.tasks.find((t) => t.id === taskId);
  const entry = snapshot.data.measured?.find((m) => m.taskId === taskId);

  // Once the draft is done, read the employee's answer from its private conversation.
  useEffect(() => {
    if (!task || task.state !== "completed" || answer) return;
    let current = true;
    api<ThreadDetail>("/company/employees/" + employeeId + "/threads/" + task.threadId)
      .then((detail) => {
        const reply = detail.messages.filter((m) => m.author === employeeId && m.sequence > task.watermark).at(-1);
        if (current && reply) setAnswer(reply.body);
      })
      .catch((e: Error) => current && setError(e.message));
    return () => {
      current = false;
    };
  }, [task?.state, task?.threadId, task?.watermark, employeeId, answer, task]);

  async function read() {
    const list = sources
      .split(/\r?\n|,\s*/)
      .map((s) => s.trim())
      .filter(Boolean);
    if (list.length === 0) {
      setError("Name at least one source: a web address or a GitHub repository.");
      return;
    }
    setReading(true);
    setError("");
    setResults([]);
    setAnswer("");
    setFilled(false);
    try {
      const result = await api<{ sources: SourceRead[]; attempt: { taskId: string } }>(
        "/company/context-sources",
        "POST",
        { requestId: crypto.randomUUID(), employeeId, sources: list, creditAllowance: Number(allowance), scope },
      );
      setResults(result.sources);
      setTaskId(result.attempt.taskId);
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setReading(false);
    }
  }

  const parsed = answer ? parseDraft(answer, fields) : null;
  return (
    <section className="co-from-sources" aria-label="Fill from sources">
      <h4>Fill from sources</h4>
      <p className="co-muted">
        Name your website, GitHub repositories or blog posts. HireZero reads them without AI, then {employeeName} drafts
        every field in one turn you authorize. You edit and approve everything before anyone uses it.
      </p>
      <label>
        Sources
        <textarea
          rows={3}
          value={sources}
          placeholder={"https://example.com\nowner/repository\nhttps://example.com/blog/a-post"}
          onChange={(e) => setSources(e.target.value)}
        />
      </label>
      <div className="co-from-sources-row">
        <label>
          {cents ? "Spend cap (US cents)" : "Credit allowance"}
          <input
            type="number"
            min="0.01"
            max="1000"
            step="0.01"
            value={allowance}
            onChange={(e) => setAllowance(e.target.value)}
          />
        </label>
        <button type="button" className="co-primary" disabled={reading} onClick={() => void read()}>
          {reading ? "Reading sources…" : "Read sources"}
        </button>
      </div>
      {results.length > 0 && (
        <ul className="co-source-results" aria-label="Sources read">
          {results.map((r) => (
            <li key={r.source} className={r.read ? "on" : "off"}>
              <b>{r.title || r.source}</b>{" "}
              <small className="co-muted">
                {r.read
                  ? r.characters.toLocaleString() + " characters" + (r.clipped ? ", clipped" : "")
                  : r.error || "Not read"}
              </small>
            </li>
          ))}
        </ul>
      )}
      {error && <p role="alert">{error}</p>}
      {task && !answer && (
        <MeasuredJob
          task={task}
          entry={entry}
          available={!!snapshot.capabilities.measuredOneJob}
          cents={cents}
          busy={reading}
          prepare={async (action, values) => {
            const result = await command(action, values);
            await refresh();
            return result;
          }}
          refresh={refresh}
        />
      )}
      {parsed && (
        <div className="co-draft-preview" aria-label="Drafted context">
          <p>
            <b>{employeeName}’s draft</b> fills {Object.keys(parsed).length} of {fields.length} fields. Review and edit
            them below before saving.
          </p>
          <dl>
            {Object.entries(parsed).map(([field, value]) => (
              <div key={field}>
                <dt>{humanize(field)}</dt>
                <dd>{value}</dd>
              </div>
            ))}
          </dl>
          <button
            type="button"
            className="co-primary"
            onClick={() => {
              onFill(parsed);
              setFilled(true);
            }}
          >
            {filled ? "Filled: edit the fields below" : "Fill the fields with this draft"}
          </button>
        </div>
      )}
      {answer && !parsed && (
        <div className="co-draft-preview">
          <p>The draft did not come back as the expected fields, so nothing was filled. Here it is as written:</p>
          <pre>{answer}</pre>
        </div>
      )}
    </section>
  );
}
