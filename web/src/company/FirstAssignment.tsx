import { memberStorage as sessionStorage } from "../lib/api";
import { useRef, useState } from "react";
import { company } from "../lib/api";
import { command, reconcileCompanyOperation, UnconfirmedCompanyOperation, type Snapshot, type Work } from "./api";
import type { BusinessSetup } from "../roles/company";
import { ModelChoice } from "./ModelChoice";
import { defaultAllowance, SpendLimit } from "./allowance";

type Draft = { prompt: string; allowance: string; modelRoute?: string; pending?: string; prepared?: boolean };
export function FirstAssignment({
  snapshot,
  employeeId,
  threadId,
  setup,
  refresh,
  open,
  openWork,
}: {
  snapshot: Snapshot;
  employeeId: string;
  threadId: string;
  setup: BusinessSetup;
  refresh: () => Promise<void>;
  open: (employee: string, thread: string) => void;
  openWork: (task: Work) => void;
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
    [checking, setChecking] = useState(false),
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
    setChecking(!!current.current.pending);
    setBusy(true);
    setError("");
    try {
      let saved: unknown;
      if (current.current.pending) saved = await reconcileCompanyOperation(current.current.pending);
      else {
        const allowance = Number(cents ? current.current.allowance || defaultAllowance(true) : defaultAllowance(false));
        if (!current.current.prompt.trim() || !(allowance > 0 && allowance <= 1000))
          throw new Error("Enter the request, and a spend limit between $0.01 and $10.00.");
        saved = await command(
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
        const task = (saved as { task?: Work })?.task;
        if (
          task?.id &&
          task.employeeId === employeeId &&
          task.threadId === threadId &&
          task.skill === setup.firstAssignment.skill
        )
          openWork(task);
        else open(employeeId, threadId);
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
        {setup.firstAssignment.description} Review the request before authorizing one draft. Then approve the result and
        mark the assignment complete.
      </p>
      {error && (
        <p className="co-error" role="alert">
          {error}
        </p>
      )}
      {draft.pending ? (
        <>
          <p role="status">
            {busy
              ? checking
                ? "Checking your saved request…"
                : "Preparing your request for review…"
              : "The save needs confirmation. Your request is kept; it will not be repeated."}
          </p>
          <button disabled={busy} onClick={() => void prepare()}>
            {busy ? (checking ? "Checking status…" : "Preparing request…") : "Check first request status"}
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
          <button
            className="co-primary"
            disabled={busy}
            onClick={() => (assignment ? openWork(assignment) : open(employeeId, threadId))}
          >
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
            <SpendLimit
              cents={cents}
              value={draft.allowance || defaultAllowance(true)}
              onChange={(allowance) => change({ ...draft, allowance })}
            />
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
