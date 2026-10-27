import { memberStorage as sessionStorage } from "../lib/api";
import { useRef, useState } from "react";
import { company } from "../lib/api";
import { command, reconcileCompanyOperation, UnconfirmedCompanyOperation, type Snapshot } from "./api";
import type { BusinessSetup } from "../roles/company";
import { ModelChoice } from "./ModelChoice";

type Draft = { prompt: string; allowance: string; modelRoute?: string; pending?: string; prepared?: boolean };
export function FirstAssignment({
  snapshot,
  employeeId,
  threadId,
  setup,
  refresh,
  open,
}: {
  snapshot: Snapshot;
  employeeId: string;
  threadId: string;
  setup: BusinessSetup;
  refresh: () => Promise<void>;
  open: (employee: string, thread: string) => void;
}) {
  const key = `hirezero-first-assignment-v1:${company() || "local-company"}:${employeeId}`;
  const [draft, setDraft] = useState<Draft>(() => {
    try {
      const saved = JSON.parse(sessionStorage.getItem(key) || "null");
      if (saved && typeof saved.prompt === "string" && typeof saved.allowance === "string") return saved;
    } catch {
      /* The form still works without browser storage. */
    }
    return { prompt: setup.firstAssignment.prompt, allowance: "1" };
  });
  const [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const locked = useRef(false),
    current = useRef(draft);
  current.current = draft;
  function change(next: Draft) {
    current.current = next;
    setDraft(next);
    try {
      sessionStorage.setItem(key, JSON.stringify(next));
    } catch {
      /* Keep the current form in memory. */
    }
  }
  const assignment = snapshot.data.tasks.find(
    (t) => t.employeeId === employeeId && t.skill === setup.firstAssignment.skill,
  );
  const cents =
    (snapshot.capabilities.modelChoices?.find((option) => option.route === draft.modelRoute)?.allowanceUnit ??
      snapshot.capabilities.allowanceUnit) === "usd-cents";
  async function prepare() {
    if (locked.current) return;
    locked.current = true;
    setBusy(true);
    setError("");
    try {
      if (current.current.pending) await reconcileCompanyOperation(current.current.pending);
      else {
        const allowance = Number(current.current.allowance);
        if (!current.current.prompt.trim() || !(allowance > 0 && allowance <= 1000))
          throw new Error("Enter the request and a positive allowance up to 1,000.");
        await command(
          "assignment.prepare",
          {
            employeeId,
            threadId,
            skill: setup.firstAssignment.skill,
            body: current.current.prompt.trim(),
            creditAllowance: allowance,
            modelRoute: current.current.modelRoute || null,
            id: setup.companyKnowledge?.id,
            expectedVersion: setup.companyKnowledge?.version,
            digest: setup.companyKnowledge?.digest,
          },
          (requestId) => change({ ...current.current, pending: requestId }),
        );
      }
      change({ ...current.current, pending: undefined, prepared: true });
      try {
        await refresh();
        open(employeeId, threadId);
      } catch {
        setError("Your request was saved. Open the private chat to review it when the connection recovers.");
      }
    } catch (e) {
      if (!(e instanceof UnconfirmedCompanyOperation) && !draft.pending)
        change({ ...current.current, pending: undefined });
      setError((e as Error).message);
    } finally {
      locked.current = false;
      setBusy(false);
    }
  }
  return (
    <section className="co-first-assignment" aria-label="First assignment">
      <h3>{setup.firstAssignment.title}</h3>
      <p>
        {setup.firstAssignment.description} Review the request and allowance before authorizing one draft. Then approve
        the result and mark the assignment complete.
      </p>
      {error && (
        <p className="co-error" role="alert">
          {error}
        </p>
      )}
      {draft.pending ? (
        <>
          <p role="status">The save needs confirmation. Your request is kept; it will not be repeated.</p>
          <button disabled={busy} onClick={() => void prepare()}>
            Check first request status
          </button>
        </>
      ) : draft.prepared || assignment ? (
        <>
          <p role="status">
            {assignment?.state === "completed"
              ? "Your first plan is complete. Open it to review or reuse the approved result."
              : assignment?.state === "awaiting_review"
                ? "Your first plan is ready for review. Approve the result before marking the assignment complete."
                : "Your saved assignment is in the private chat. Review its current status there."}
          </p>
          <button className="co-primary" disabled={busy} onClick={() => open(employeeId, threadId)}>
            Open first assignment
          </button>
        </>
      ) : (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            void prepare();
          }}
        >
          <fieldset disabled={busy || !snapshot.capabilities.measuredOneJob}>
            <ModelChoice
              options={snapshot.capabilities.modelChoices}
              value={draft.modelRoute ?? ""}
              onChange={(modelRoute) => change({ ...draft, modelRoute, allowance: "" })}
            />
            <label>
              First request
              <textarea
                required
                maxLength={4000}
                value={draft.prompt}
                onChange={(e) => change({ ...draft, prompt: e.target.value })}
              />
            </label>
            <label>
              {cents ? "Spend cap (US cents)" : "Intended credit allowance"}
              <input
                type="number"
                min="0.01"
                max="1000"
                step="0.01"
                required
                value={draft.allowance}
                onChange={(e) => change({ ...draft, allowance: e.target.value })}
              />
            </label>
            {!cents && <p className="co-muted">This is an intended allowance, not a provider-enforced spending cap.</p>}
            <button className="co-primary" type="submit">
              {busy ? "Preparing request…" : "Prepare my first plan"}
            </button>
          </fieldset>
          {!snapshot.capabilities.measuredOneJob && (
            <p>Connect a model in Company → Integrations to prepare this request.</p>
          )}
        </form>
      )}
    </section>
  );
}
