import { useState } from "react";
import { LoaderCircle, Sparkles } from "lucide-react";
import { api, ApiError } from "../../shell/api";
import { preparationStages, type Preparation, type PreparationStage, type PreparationSuggestion } from "./types";

interface PreparationPanelProps {
  requestId: string;
  preparation?: Preparation;
  onRefresh: () => Promise<void>;
  onForbidden: () => void;
}
export function PreparationPanel({ requestId, preparation, onRefresh, onForbidden }: PreparationPanelProps) {
  const [stage, setStage] = useState<PreparationStage>(preparation?.stage || "intake"),
    [blocker, setBlocker] = useState(preparation?.blocker || ""),
    [nextAction, setNextAction] = useState(preparation?.nextAction || "");
  const [suggestion, setSuggestion] = useState<PreparationSuggestion | null>(null),
    [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [notice, setNotice] = useState("");
  async function run(action: () => Promise<void>) {
    if (busy) return;
    setBusy(true);
    setError("");
    setNotice("");
    try {
      await action();
    } catch (cause) {
      setError((cause as Error).message);
      if (cause instanceof ApiError && cause.status === 403) onForbidden();
    } finally {
      setBusy(false);
    }
  }
  function review() {
    if (!suggestion) return;
    setStage(suggestion.suggestion.stage);
    setBlocker(suggestion.suggestion.blocker);
    setNextAction(suggestion.suggestion.nextAction);
    setNotice("Suggestion copied into your review fields. Save when the wording is right.");
  }
  return (
    <section className="fe-stack">
      <div>
        <h3>Unblock the work</h3>
        <p className="fe-muted">
          Ask OpenClaw to find unclear scope, missing evidence, and the questions that would move this request forward.
          The result stays private.
        </p>
        <button
          disabled={busy}
          onClick={() =>
            void run(async () => {
              setSuggestion(
                await api<PreparationSuggestion>("/requests/" + requestId + "/preparation/suggest", "POST", {}),
              );
            })
          }
        >
          {busy ? <LoaderCircle size={15} className="fe-spin" /> : <Sparkles size={15} />}Find scope and information
          gaps
        </button>
      </div>
      {suggestion && (
        <div className="dev-suggestion">
          <h4>Agent recommendation · review needed</h4>
          <p>{preparationStages.find(([key]) => key === suggestion.suggestion.stage)?.[1]}</p>
          <p>
            <strong>Blocker:</strong> {suggestion.suggestion.blocker || "No blocker identified; verify readiness."}
          </p>
          <p>
            <strong>Next action:</strong> {suggestion.suggestion.nextAction}
          </p>
          {suggestion.suggestion.questions.length > 0 && (
            <ol>
              {suggestion.suggestion.questions.map((question) => (
                <li key={question}>{question}</li>
              ))}
            </ol>
          )}
          <p className="fe-muted">
            SemaForge context: {suggestion.sources.semaForge} · Wiki: {suggestion.sources.wiki}
          </p>
          <button disabled={busy} onClick={review}>
            Use in review fields
          </button>
        </div>
      )}
      <form
        className="fe-form"
        onSubmit={(event) => {
          event.preventDefault();
          void run(async () => {
            await api("/requests/" + requestId + "/preparation", "PUT", { stage, blocker, nextAction });
            await onRefresh();
            setNotice("Private preparation queue updated.");
          });
        }}
      >
        <label>
          Preparation stage
          <select value={stage} onChange={(event) => setStage(event.target.value as PreparationStage)}>
            {preparationStages.map(([key, text]) => (
              <option key={key} value={key}>
                {text}
              </option>
            ))}
          </select>
        </label>
        <label>
          What is blocking progress?
          <textarea
            maxLength={1000}
            rows={3}
            value={blocker}
            onChange={(event) => setBlocker(event.target.value)}
            placeholder="Unclear acceptance criteria, missing repro, repository access, a decision…"
          />
        </label>
        <label>
          Next action
          <textarea
            maxLength={1000}
            rows={3}
            value={nextAction}
            onChange={(event) => setNextAction(event.target.value)}
            placeholder="The smallest step that would unblock this request"
          />
        </label>
        <footer>
          <button className="primary" disabled={busy}>
            Save private preparation
          </button>
        </footer>
      </form>
      {error && (
        <p className="fe-alert" role="alert">
          {error}
        </p>
      )}
      {notice && <p role="status">{notice}</p>}
    </section>
  );
}
