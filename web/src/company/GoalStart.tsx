import { useEffect, useRef, useState } from "react";
import { api, company, memberStorage } from "../lib/api";
import { approvedBusinessProfile, recommendationsForProfile, setupForProfile } from "../roles/company";
import {
  command,
  reconcileCompanyOperation,
  UnconfirmedCompanyOperation,
  type BusinessProfile,
  type Snapshot,
  type Work,
} from "./api";
import { ModelChoice, providerName } from "./ModelChoice";
import { advanceGoal, type GoalFlow, type GoalTerms } from "./goalFlow";
import { goalWebsite } from "./goalWebsite";
import "./goalStart.css";

type Draft = {
  name: string;
  business: string;
  goal: string;
  modelRoute: string;
  allowance: string;
  website?: string;
  flow?: GoalFlow;
  pending?: string;
  taskId?: string;
};

export function GoalStart({
  snapshot,
  refresh,
  openWork,
  onBusiness,
  onResumeHire,
}: {
  snapshot: Snapshot;
  refresh: () => Promise<void>;
  openWork: (task: Work) => void;
  onBusiness?: () => void;
  onResumeHire?: (employee: Snapshot["data"]["employees"][number]) => void;
}) {
  const profile = approvedBusinessProfile(snapshot);
  const key = "hirezero-goal-start-v1:" + (company() || "local-company");
  const [draft, setDraft] = useState<Draft>(() => {
    try {
      const stored = JSON.parse(memberStorage.getItem(key) || "null");
      if (stored && ["name", "business", "goal", "modelRoute", "allowance"].every((k) => typeof stored[k] === "string"))
        return stored;
    } catch {
      /* An empty form is better than pretending a lost request completed. */
    }
    return {
      name: snapshot.data.business?.name || "",
      business: "",
      goal: snapshot.data.business?.goal || "",
      modelRoute: "",
      allowance: snapshot.capabilities.allowanceUnit === "usd-cents" ? "5" : "1",
    };
  });
  const [busy, setBusy] = useState(false),
    [checking, setChecking] = useState(false),
    [error, setError] = useState("");
  const [consent, setConsent] = useState(false);
  const [terms, setTerms] = useState<GoalTerms>();
  const [termsError, setTermsError] = useState("");
  const [termsRequest, setTermsRequest] = useState(0);
  const website = goalWebsite(draft.website || "");
  const current = useRef(draft),
    locked = useRef(false);
  current.current = draft;
  function change(next: Draft, required = false) {
    if (required) memberStorage.setItem(key, JSON.stringify(next));
    else
      try {
        memberStorage.setItem(key, JSON.stringify(next));
      } catch {
        /* Edits remain visible in this tab. */
      }
    current.current = next;
    setDraft(next);
  }
  const proposal: BusinessProfile = {
    ...(profile || { id: "goal-preview", businessVersion: 0, digest: "", state: "draft", version: 0, updatedAt: "" }),
    fields: (
      profile?.fields ||
      [
        "business",
        "services",
        "customers",
        "voice",
        "market",
        "competitors",
        "opportunities",
        "goals",
        "boundaries",
      ].map((key) => ({
        key,
        value: key === "business" ? draft.business : "Unknown",
        verification: key === "business" ? "owner" : "unknown",
        citations: [],
      }))
    ).map((field) =>
      field.key === "goals" ? { ...field, value: draft.goal, verification: "owner", citations: [] } : field,
    ),
  };
  const selected = recommendationsForProfile(snapshot.data.roles, proposal)[0]?.role;
  const setup = selected ? setupForProfile(proposal, selected) : undefined;
  const prompt = setup
    ? setup.firstAssignment.prompt +
      "\nDeliver the most useful complete first draft with the available facts. Fill noncritical gaps with clearly labeled, reversible assumptions, never invented factual claims. Do not delay the draft for questions. Put only decisions that materially affect its use at the end."
    : "";
  const employee = snapshot.data.employees.find((e) => e.roleId === selected?.id && e.state === "active");
  const unfinished =
    !employee && snapshot.data.employees.find((e) => e.roleId === selected?.id && e.state === "onboarding");
  const knowledge = snapshot.data.knowledge.find((k) => k.id === profile?.knowledgeId);
  const partialBrief = !profile && (!!snapshot.data.business || snapshot.data.knowledge.some((k) => !k.departmentId));
  const saved = snapshot.data.tasks.find((t) => t.id === draft.taskId);
  const activeJob = snapshot.data.tasks.find(
    (t) =>
      t.employeeId === employee?.id &&
      snapshot.data.measured?.some(
        (m) => m.taskId === t.id && ["claimed", "reserved", "dispatching"].includes(m.phase),
      ),
  );
  const cents =
    (snapshot.capabilities.modelChoices?.find((c) => c.route === draft.modelRoute)?.allowanceUnit ??
      snapshot.capabilities.allowanceUnit) === "usd-cents";

  useEffect(() => {
    let active = true;
    setTerms(undefined);
    setTermsError("");
    setConsent(false);
    if (selected && setup && snapshot.capabilities.measuredOneJob) {
      const query = new URLSearchParams({ roleId: selected.id, skill: setup.firstAssignment.skill });
      if (draft.modelRoute) query.set("modelRoute", draft.modelRoute);
      void api<GoalTerms>("/company/goals/terms?" + query)
        .then((value) => {
          if (active) setTerms(value);
        })
        .catch((e) => {
          if (active) setTermsError((e as Error).message);
        });
    }
    return () => {
      active = false;
    };
  }, [
    selected?.id,
    setup?.firstAssignment.skill,
    draft.modelRoute,
    snapshot.capabilities.measuredOneJob,
    termsRequest,
  ]);
  useEffect(() => setConsent(false), [draft.goal, draft.business, draft.name, draft.website, draft.allowance]);

  async function prepare() {
    if (locked.current || !setup || !selected) return;
    locked.current = true;
    setBusy(true);
    setError("");
    const wasPending = !!current.current.pending;
    setChecking(wasPending);
    try {
      let result: unknown;
      if (current.current.pending) result = await reconcileCompanyOperation(current.current.pending);
      else
        result = await command(
          "goal.prepare",
          {
            decision: "use-details-and-prepare",
            id: knowledge?.id,
            expectedVersion: knowledge?.version || 0,
            digest: knowledge?.digest,
            roleId: selected.id,
            employeeId: employee?.id,
            skill: setup.firstAssignment.skill,
            creditAllowance: Number(current.current.allowance),
            modelRoute: current.current.modelRoute || null,
            body: JSON.stringify({
              businessName: current.current.name.trim() || "My business",
              businessDescription: current.current.business.trim(),
              goal: current.current.goal.trim(),
              employeeName: selected.onboarding?.name || selected.name,
              departmentName: selected.onboarding?.department || selected.name,
              responsibility: setup.responsibility,
              values: setup.values,
              prompt,
            }),
          },
          (requestId) => change({ ...current.current, pending: requestId }, true),
        );
      const task = (result as { task?: Work })?.task;
      if (!task?.id) throw new Error("The saved work could not be identified. Check its status before starting again.");
      change({ ...current.current, pending: undefined, taskId: task.id });
      await refresh();
      openWork(task);
    } catch (e) {
      if (!wasPending && !(e instanceof UnconfirmedCompanyOperation))
        change({ ...current.current, pending: undefined });
      setError((e as Error).message);
    } finally {
      locked.current = false;
      setBusy(false);
    }
  }

  async function generate() {
    if (locked.current || (!current.current.flow && (!consent || !terms || !setup || !selected || website.error)))
      return;
    locked.current = true;
    setBusy(true);
    setError("");
    try {
      let flow = current.current.flow;
      if (!flow) {
        flow = {
          stage: "context",
          terms: terms!,
          fields: {
            decision: "use-details-and-prepare",
            id: knowledge?.id,
            expectedVersion: knowledge?.version || 0,
            digest: knowledge?.digest,
            roleId: selected!.id,
            employeeId: employee?.id,
            skill: setup!.firstAssignment.skill,
            creditAllowance: Number(current.current.allowance),
            modelRoute: terms!.route,
          },
          input: {
            businessName: current.current.name.trim() || "My business",
            businessDescription: current.current.business.trim(),
            goal: current.current.goal.trim(),
            website: website.url,
            employeeName: selected!.onboarding?.name || selected!.name,
            departmentName: selected!.onboarding?.department || selected!.name,
            responsibility: setup!.responsibility,
            values: setup!.values,
            prompt,
          },
        };
        change({ ...current.current, flow }, true);
      }
      const task = await advanceGoal(flow, (next) => change({ ...current.current, flow: next }, true));
      if (!task) return;
      change({ ...current.current, flow: undefined, taskId: task.id }, true);
      await refresh();
      openWork(task);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      locked.current = false;
      setBusy(false);
    }
  }

  async function editStoppedGoal() {
    setBusy(true);
    try {
      await refresh();
      change({ ...current.current, flow: undefined }, true);
      setConsent(false);
      setError("");
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  const progress = {
    context: "Setting up your business…",
    lookup: "Reading your website…",
    sources: "Checking saved website context…",
    prepare: "Preparing your request…",
    run: "Writing your first draft…",
    done: "Opening your draft…",
  };
  return (
    <section className="co-goal-start" aria-label="Start with a goal">
      <p className="co-eyebrow">Put your team to work</p>
      <h2>What would you like done?</h2>
      <p>Describe the outcome. Your specialist and first draft are next.</p>
      {error && (
        <p className="co-error" role="alert">
          {error}
        </p>
      )}
      {draft.flow ? (
        <div role="status">
          <p>
            {busy
              ? progress[draft.flow.stage]
              : draft.flow.stopped
                ? "Your request stopped before another action was taken."
                : "Your request is saved. Check its status to continue the same run."}
          </p>
          {draft.flow.stopped ? (
            <button
              className="co-primary"
              disabled={busy}
              onClick={() => (draft.flow?.prepared ? openWork(draft.flow.prepared.task) : void editStoppedGoal())}
            >
              {draft.flow.prepared ? "Review saved work" : "Edit request"}
            </button>
          ) : (
            <button className="co-primary" disabled={busy} onClick={() => void generate()}>
              {busy ? "Working…" : "Check saved request"}
            </button>
          )}
        </div>
      ) : draft.pending ? (
        <div role="status">
          <p>
            {busy
              ? checking
                ? "Checking your saved request…"
                : "Preparing your draft…"
              : "Your request is saved in this browser. Check its status to continue."}
          </p>
          <button className="co-primary" disabled={busy} onClick={() => void prepare()}>
            {busy ? (checking ? "Checking…" : "Preparing…") : "Check saved request"}
          </button>
        </div>
      ) : draft.taskId ? (
        <div>
          <p>
            {saved && ["completed", "awaiting_review"].includes(saved.state)
              ? "Your draft is ready to read, copy or refine."
              : saved
                ? "Your request is ready. Open it to review the run or follow its progress."
                : "Your work was saved. Reload the saved view to open it."}
          </p>
          <button
            className="co-primary"
            onClick={() => (saved ? openWork(saved) : void refresh().catch((e) => setError((e as Error).message)))}
          >
            {saved ? "Continue my work" : "Reload saved work"}
          </button>
          {saved && (
            <button onClick={() => change({ ...draft, taskId: undefined, goal: "" })}>Start another goal</button>
          )}
        </div>
      ) : partialBrief ? (
        <div>
          <p>You already have a business brief in progress. Finish that review so we can use your saved details.</p>
          <button className="co-primary" onClick={onBusiness}>
            Continue my business brief
          </button>
        </div>
      ) : (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            void generate();
          }}
        >
          <fieldset disabled={busy}>
            {!profile && (
              <label>
                What does your business do?
                <textarea
                  required
                  maxLength={600}
                  rows={2}
                  value={draft.business}
                  placeholder="We make custom birthday cakes for families in our town."
                  onChange={(e) => change({ ...draft, business: e.target.value })}
                />
              </label>
            )}
            <label>
              Your goal
              <textarea
                required
                maxLength={600}
                rows={2}
                value={draft.goal}
                placeholder="Prepare three useful posts for this week that show how our cake ordering works."
                onChange={(e) => change({ ...draft, goal: e.target.value })}
              />
            </label>
            <label>
              Website (optional)
              <input
                type="text"
                inputMode="url"
                autoComplete="url"
                autoCapitalize="none"
                spellCheck={false}
                ref={(input) => input?.setCustomValidity(website.error)}
                maxLength={800}
                placeholder="your-business.example"
                value={draft.website || ""}
                onChange={(e) => change({ ...draft, website: e.target.value })}
              />
            </label>
            <p className="co-muted">
              {profile ? "Uses your approved brief." : "Uses your details; missing facts stay unknown."}
              {website.url
                ? ` Reads up to three public pages from ${website.url} as unverified references for this draft.`
                : ""}
            </p>
            {terms ? (
              <div className="co-goal-terms">
                <strong>
                  {providerName(terms.terms?.provider || "host")} · {terms.route}
                </strong>
                <p>
                  One private draft · {draft.allowance}{" "}
                  {terms.terms?.allowanceUnit === "usd-cents" ? "US cents maximum" : "intended existing credit(s)"} ·{" "}
                  {terms.policy.localDeadlineSeconds}s local deadline.
                </p>
                <small>
                  {terms.terms?.provider === "codex-oauth"
                    ? `Reasoning effort: ${terms.policy.effort || "low"} · ${terms.policy.outputTarget.toLocaleString()} output-token target. Credit, time and token limits are soft.`
                    : `Output target and deadline are planning limits; provider terms apply.`}{" "}
                  No publishing or automatic retry.
                </small>
                <label className="co-check">
                  <input type="checkbox" checked={consent} onChange={(e) => setConsent(e.target.checked)} />
                  <span>
                    Use these details{draft.website ? " and this website" : ""} and run this one draft under the terms
                    above.
                  </span>
                </label>
              </div>
            ) : termsError ? (
              <div>
                <p className="co-error" role="alert">
                  Couldn’t load the model and run terms. {termsError}
                </p>
                <button type="button" onClick={() => setTermsRequest((request) => request + 1)}>
                  Retry loading terms
                </button>
              </div>
            ) : (
              <p role="status">Loading the model and run terms…</p>
            )}
            {unfinished ? (
              <button type="button" className="co-primary" onClick={() => onResumeHire?.(unfinished)}>
                Continue {unfinished.name}’s setup
              </button>
            ) : activeJob ? (
              <button type="button" className="co-primary" onClick={() => openWork(activeJob)}>
                Open current work
              </button>
            ) : (
              <button
                className="co-primary"
                type="submit"
                disabled={!setup || !terms || !consent || !snapshot.capabilities.measuredOneJob}
              >
                {busy ? "Writing your draft…" : "Generate my first draft"}
              </button>
            )}
            {activeJob && (
              <p>{employee?.name} already has a request awaiting authorization or completion. Open it to continue.</p>
            )}
            {setup && (
              <div className="co-goal-output">
                <strong>{setup.firstAssignment.title}</strong>
                <p>{setup.firstAssignment.description}</p>
                <small>
                  {employee
                    ? `${employee.name} will handle this.`
                    : `We’ll set up a ${selected?.name} specialist for this.`}
                </small>
              </div>
            )}
            <details>
              <summary>Adjust setup and model</summary>
              {!profile && (
                <label>
                  Business name (optional)
                  <input
                    maxLength={100}
                    value={draft.name}
                    onChange={(e) => change({ ...draft, name: e.target.value })}
                  />
                </label>
              )}
              <ModelChoice
                options={snapshot.capabilities.modelChoices}
                value={draft.modelRoute}
                onChange={(modelRoute) =>
                  change({
                    ...draft,
                    modelRoute,
                    allowance:
                      snapshot.capabilities.modelChoices?.find((c) => c.route === modelRoute)?.allowanceUnit ===
                      "usd-cents"
                        ? "5"
                        : "1",
                  })
                }
              />
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
              {setup && (
                <>
                  <p>{setup.responsibility}</p>
                  <details>
                    <summary>Suggested working instructions</summary>
                    <dl>
                      {Object.entries(setup.values).map(([key, value]) => (
                        <div key={key}>
                          <dt>{key}</dt>
                          <dd>{value}</dd>
                        </div>
                      ))}
                    </dl>
                    <p>{prompt}</p>
                  </details>
                </>
              )}
            </details>
            {!snapshot.capabilities.measuredOneJob && (
              <p>Connect a model in Company → Integrations to create drafts.</p>
            )}
          </fieldset>
        </form>
      )}
    </section>
  );
}
