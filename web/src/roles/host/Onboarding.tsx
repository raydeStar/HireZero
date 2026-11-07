import { useEffect, useRef, useState } from "react";
import { api, company } from "../../lib/api";
import {
  command,
  type BusinessDraftJob,
  type BusinessIdentity,
  type BusinessLookup,
  type BusinessProfile,
  type Employee,
  type ProfileField,
  type Role,
  type ThreadDetail,
} from "../../company/api";
import { CompanyConversation } from "../../company/CompanyConversation";
import { EmployeeSays, Working } from "../../company/EmployeeSays";
import { advanceGoal, type GoalFlow, type GoalTerms } from "../../company/goalFlow";
import { defaultAllowance, spendText } from "../../company/allowance";
import { providerName } from "../../company/ModelChoice";
import { approvedBusinessProfile, recommendationsForProfile, setupForProfile, type OnboardingProps } from "../company";
import "./onboarding.css";

const labels: Record<string, string> = {
  business: "What you do",
  services: "Products and services",
  customers: "Who you serve",
  voice: "How you sound",
  market: "Market and location",
  competitors: "Competitors",
  opportunities: "Ideas to try",
  goals: "What you want",
  boundaries: "Things to avoid saying",
};
const keys = Object.keys(labels);
const source: Record<string, string> = {
  owner: "your words",
  sourced: "from your site",
  hypothesis: "my guess",
  unknown: "not known yet",
};
const storeKey = () => "hirezero-onboarding-v1:" + (company() || "local-company");
type Saved = { flow?: GoalFlow };
const approvalMessage = "Looks right, that’s us.";
function load(): Saved {
  try {
    return JSON.parse(localStorage.getItem(storeKey()) || "{}") as Saved;
  } catch {
    return {};
  }
}
function save(next: Saved) {
  try {
    localStorage.setItem(storeKey(), JSON.stringify(next));
  } catch {
    /* Onboarding still works; a reload may ask the needs question again. */
  }
}
const urlIn = (text: string) =>
  /\b(?:https?:\/\/)?(?:[a-z0-9-]+\.)+[a-z]{2,}(?:\/[^\s]*)?/i.exec(text)?.[0]?.replace(/[.,;)]+$/, "") ?? "";
const short = (text: string, max: number) =>
  text.length <= max ? text : text.slice(0, text.lastIndexOf(" ", max - 1));
// People type "example.com"; the business record wants a full https address.
const site = (text: string) => {
  const value = text.trim();
  if (!value) return "";
  return /^https?:\/\//i.test(value) ? value.replace(/^http:/i, "https:") : "https://" + value;
};
const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// A new company's first screen (docs/adr/0186): HireZero greets the owner in chat, learns the business, reads its site,
// drafts the company brief for the owner to correct and approve, asks what they need, and brings in the first teammate.
// Every reply is the owner's own Send; research and hiring are buttons with their own sign-off, and the hiring button
// shows the model and the most the first draft can cost before it is pressed.
export function Onboarding(props: OnboardingProps) {
  const { snapshot, refresh, run, onDone, onUseForm } = props;
  const d = snapshot.data;
  const role = d.roles.find((r) => r.uiSlots?.includes("concierge"));
  const host = d.employees.find((e) => e.roleId === role?.id && e.state !== "retired");
  const thread = host && d.threads.find((t) => t.employeeId === host.id && (t.scope ?? "employee") === "employee");
  const [detail, setDetail] = useState<ThreadDetail | null>(null);
  const [error, setError] = useState("");
  const [working, setWorking] = useState("");
  const [, setSaved] = useState<Saved>(load);
  const locked = useRef(false);
  const remember = (patch: Saved) => {
    const next = { ...load(), ...patch };
    save(next);
    setSaved(next);
  };
  useEffect(() => {
    if (!host || !thread) return;
    let live = true;
    api<ThreadDetail>("/company/employees/" + host.id + "/threads/" + thread.id)
      .then((next) => live && setDetail(next))
      .catch(() => {});
    return () => {
      live = false;
    };
  }, [host?.id, thread?.id, d.cursor]);

  // One step at a time, with nothing repeated: a lost reply is checked, never sent twice.
  async function step(label: string, work: () => Promise<void>) {
    if (locked.current) return;
    locked.current = true;
    setError("");
    setWorking(label);
    try {
      await work();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      locked.current = false;
      setWorking("");
      await refresh().catch(() => {});
    }
  }

  // HireZero joins the company the first time the owner answers.
  async function start(name: string) {
    await step("Getting set up…", async () => {
      let current = (await api<{ data: typeof d }>("/company")).data;
      let department = current.departments.find((x) => x.name === "HireZero");
      if (!department) department = (await command("department.create", { name: "HireZero" })) as typeof department;
      let guide = current.employees.find((e) => e.roleId === role!.id && e.state !== "retired");
      if (!guide)
        guide = (await command("employee.hire", {
          name: "HireZero",
          roleId: role!.id,
          departmentId: department!.id,
          responsibility: "Set up the company and bring in its first teammate.",
        })) as Employee;
      if (guide.state === "onboarding") {
        const knowledge = (await command("knowledge.save", { departmentId: department!.id, body: "{}" })) as {
          id: string;
          version: number;
          digest: string;
        };
        await command("knowledge.review", {
          id: knowledge.id,
          expectedVersion: knowledge.version,
          digest: knowledge.digest,
          decision: "approve",
        });
      }
      current = (await api<{ data: typeof d }>("/company")).data;
      const chat = current.threads.find((t) => t.employeeId === guide!.id && (t.scope ?? "employee") === "employee");
      if (!chat) throw new Error("HireZero couldn’t open its conversation. Refresh and try again.");
      setWorking("");
      await say(guide.id, chat.id, name);
    });
  }

  // The owner's message to HireZero, and their sign-off for one reply, as in any conversation (ADR 0183): prepare it,
  // check the sealed context is the one just prepared, and authorize it.
  async function say(employeeId: string, threadId: string, body: string) {
    const prepared = (await command("conversation.prepare", {
      employeeId,
      threadId,
      scope: "employee",
      body,
      creditAllowance: Number(defaultAllowance(snapshot.capabilities.allowanceUnit === "usd-cents")),
      modelRoute: null,
      evidence: [],
      attachments: [],
    })) as { task?: { id: string } };
    const latest = await api<{ data: typeof d }>("/company");
    const entry = latest.data.measured?.find((m) => m.taskId === prepared.task?.id && m.job.state === "proposed");
    if (!entry) return;
    const packet = await api<{ attempt: { id: string; measured: { digest: string } } }>(
      "/company/measured/" + entry.id,
    );
    if (packet.attempt.id !== entry.id || packet.attempt.measured.digest !== entry.job.digest) return;
    await command("measured.run", {
      id: entry.id,
      expectedVersion: entry.version,
      digest: entry.job.digest,
      decision: "accept-soft-limits",
    });
  }

  const greeting = (
    <EmployeeSays name="HireZero" label="HireZero says hello">
      <p>Hi, I’m HireZero! I’ll get your company set up and bring in your first AI teammate. It takes a few minutes.</p>
      <p>To start, what’s your business called?</p>
    </EmployeeSays>
  );

  if (!host || !thread)
    return (
      <div className="co-onboard">
        <header className="co-onboard-head">
          <b>Welcome to HireZero</b>
          <button className="co-ghost" onClick={onUseForm}>
            Use the form instead
          </button>
        </header>
        <div className="co-onboard-start fe-thread">
          {greeting}
          <form
            className="co-onboard-first"
            onSubmit={(e) => {
              e.preventDefault();
              const field = e.currentTarget.elements.namedItem("business") as HTMLInputElement;
              if (field.value.trim()) void start(field.value.trim());
            }}
          >
            <label htmlFor="co-onboard-business" className="co-hidden-label">
              Your business name
            </label>
            <input
              id="co-onboard-business"
              name="business"
              maxLength={200}
              autoComplete="organization"
              placeholder="Your business name"
              disabled={!!working}
              autoFocus
            />
            <button className="co-primary" disabled={!!working || !role}>
              Send
            </button>
          </form>
          {working && <Working>{working}</Working>}
          {error && <p role="alert">{error}</p>}
        </div>
      </div>
    );

  // Where onboarding stands, from saved records only.
  const owner = (detail?.messages ?? []).filter((m) => m.author === "owner");
  const identity = d.business;
  const confirmed = identity?.state === "confirmed";
  const lookup = (d.businessLookups ?? [])
    .filter((l) => l.kind === "discovery" && l.businessVersion === identity?.version)
    .at(-1);
  const jobs = (d.businessDrafts ?? []).filter((j) => j.businessVersion === identity?.version);
  const job = jobs.at(-1);
  const profile = (d.businessProfiles ?? [])
    .filter((p) => p.businessVersion === identity?.version && ["draft", "approved"].includes(p.state))
    .at(-1);
  const approved = approvedBusinessProfile(snapshot);
  // From the owner's "Looks right" on: the approval, then what they said they need.
  let approvalAt = -1;
  owner.forEach((m, i) => {
    if (m.body === approvalMessage) approvalAt = i;
  });
  const afterApproval = approved && approvalAt >= 0 ? owner.slice(approvalAt) : [];
  const facts = {
    name: owner[0]?.body.trim() ?? "",
    website: urlIn(owner.map((m) => m.body).join(" ")),
    about: owner[1] && !urlIn(owner[1].body) ? owner[1].body.trim() : "",
    goal: owner[2]?.body.trim() ?? "",
  };

  async function note(body: string) {
    if (!detail) return;
    await run("notes.save", { employeeId: host!.id, body, expectedVersion: detail.notes.version }).catch(() => {});
  }

  // Look us up: confirm the business, read its website, and draft the brief for the owner to check.
  async function research(input: { name: string; website: string; about: string; goal: string }) {
    await step("Saving your details…", async () => {
      let business = identity;
      if (!business || business.state !== "confirmed") {
        business = (await command("business.identify", {
          expectedVersion: identity?.version ?? 0,
          body: JSON.stringify({
            name: input.name,
            website: site(input.website) || null,
            goal: input.goal,
            location: "",
            industry: "",
          }),
        })) as BusinessIdentity;
        business = (await command("business.confirm", {
          expectedVersion: business.version,
          digest: business.digest,
        })) as BusinessIdentity;
      }
      const cents = snapshot.capabilities.allowanceUnit === "usd-cents";
      let sources: string[] = [];
      const readSite = async (confirmed: BusinessIdentity) => {
        setWorking("Reading " + confirmed.website!.replace(/^https?:\/\//, "").replace(/\/$/, "") + "…");
        let read = (await command("business.lookup.start", {
          body: "discovery",
          expectedVersion: confirmed.version,
          digest: confirmed.digest,
          decision: "authorize-one-lookup",
        })) as BusinessLookup;
        for (let i = 0; i < 60 && read.state === "running"; i++) {
          await wait(2000);
          read = await api<BusinessLookup>("/company/business/lookups/" + encodeURIComponent(read.id));
        }
        return read;
      };
      if (business.website) {
        let read = await readSite(business);
        // Many sites send example.com on to www.example.com; the reader never follows a site off its confirmed
        // address, so HireZero confirms the www address once and reads that instead.
        const host = new URL(business.website).hostname;
        if (
          !read.sourceIds?.length &&
          !host.startsWith("www.") &&
          read.notices?.some((n) => n.includes("left the confirmed website"))
        ) {
          const www = business.website.replace("://" + host, "://www." + host);
          business = (await command("business.identify", {
            expectedVersion: business.version,
            body: JSON.stringify({
              name: business.name,
              website: www,
              goal: business.goal,
              location: "",
              industry: "",
            }),
          })) as BusinessIdentity;
          business = (await command("business.confirm", {
            expectedVersion: business.version,
            digest: business.digest,
          })) as BusinessIdentity;
          read = await readSite(business);
        }
        sources = (read.sourceIds ?? []).slice(0, 3);
      }
      if (sources.length) {
        setWorking("Writing your company brief…");
        await draftBrief(business, sources, cents);
      } else {
        // No site to read: the brief starts from the owner's own words, and they fill in the rest.
        const fields: ProfileField[] = keys.map((key) => ({
          key,
          value: key === "business" && input.about ? input.about : key === "goals" ? input.goal : "Unknown",
          verification: (key === "business" && input.about) || key === "goals" ? "owner" : "unknown",
          citations: [],
        }));
        await command("business.profile.save", {
          body: JSON.stringify({ businessVersion: business.version, fields }),
        });
      }
      await note(
        "Setup so far: the owner checked their details and HireZero drafted their company brief. They are reviewing it.",
      );
    });
  }

  async function draftBrief(business: BusinessIdentity, sources: string[], cents: boolean) {
    const prepared = (await command("business.draft.prepare", {
      expectedVersion: business.version,
      digest: business.digest,
      evidence: sources,
      creditAllowance: Number(defaultAllowance(cents)),
    })) as BusinessDraftJob;
    await command("business.draft.run", {
      id: prepared.id,
      expectedVersion: prepared.version,
      digest: prepared.job.digest,
      decision: "accept-soft-limits",
    });
    for (let i = 0; i < 90; i++) {
      await wait(2000);
      const latest = (await api<{ data: typeof d }>("/company")).data.businessDrafts?.find((j) => j.id === prepared.id);
      if (latest?.profileId) return;
      if (latest && ["failed", "invalid", "rejected", "withheld", "canceled", "unknown"].includes(latest.state))
        throw new Error(latest.error || "The brief draft didn’t finish. You can try again.");
    }
  }

  async function rewrite(key: string, value: string) {
    if (!profile || !identity) return;
    await step("Saving…", async () => {
      await command("business.profile.save", {
        id: profile.id,
        expectedVersion: profile.version,
        body: JSON.stringify({
          businessVersion: identity.version,
          fields: profile.fields.map((f) =>
            f.key === key
              ? {
                  key,
                  value: value.trim() || "Unknown",
                  verification: value.trim() ? "owner" : "unknown",
                  citations: [],
                }
              : f,
          ),
        }),
      });
    });
  }

  async function approve() {
    if (!profile) return;
    await step("Saving your brief…", async () => {
      await command("business.profile.review", {
        id: profile.id,
        expectedVersion: profile.version,
        digest: profile.digest,
        decision: "approve",
      });
      await note(
        "Setup so far: the company brief is approved and saved. If the owner hasn't told you yet, thank them and ask what they need help with most right now (getting found, more customers, a launch, keeping in touch with customers, anything). Once they've told you, recommend a Marketing partner as their first teammate.",
      );
      // Said in the chat, so HireZero's next question is a real reply in its place.
      setWorking("");
      await say(host!.id, thread!.id, approvalMessage);
    });
  }

  async function again() {
    if (!identity || !lookup?.sourceIds?.length) return;
    await step("Writing your company brief again…", async () => {
      await draftBrief(identity, lookup.sourceIds!.slice(0, 3), snapshot.capabilities.allowanceUnit === "usd-cents");
    });
  }

  // Yes: hire the recommended teammate with their first assignment, on the terms the offer showed, then HireZero steps
  // aside.
  async function hire(terms: GoalTerms) {
    if (!approved) return;
    const pick = recommendationsForProfile(d.roles, approved)[0]?.role;
    const setup = pick && setupForProfile(approved, pick);
    if (!pick || !setup) return setError("No teammate fits this brief yet. Use the form instead.");
    await step(`Bringing in your ${pick.onboarding?.name || pick.name}…`, async () => {
      let flow = load().flow;
      if (!flow) {
        const knowledge = d.knowledge.find((k) => k.id === approved.knowledgeId)!;
        const needs = afterApproval
          .slice(1)
          .map((m) => m.body)
          .join("\n");
        const goal = identity?.goal || approved.fields.find((f) => f.key === "goals")?.value || "Grow the business";
        flow = {
          stage: "context",
          terms,
          fields: {
            decision: "use-details-and-prepare",
            id: knowledge.id,
            expectedVersion: knowledge.version,
            digest: knowledge.digest,
            roleId: pick.id,
            skill: setup.firstAssignment.skill,
            creditAllowance: Number(defaultAllowance(snapshot.capabilities.allowanceUnit === "usd-cents")),
            modelRoute: terms.route,
          },
          input: {
            businessName: identity?.name || "My business",
            businessDescription: approved.fields.find((f) => f.key === "business")?.value || "",
            goal: short(goal, 600),
            employeeName: pick.onboarding?.name || pick.name,
            departmentName: pick.onboarding?.department || pick.name,
            responsibility: setup.responsibility,
            values: setup.values,
            prompt:
              setup.firstAssignment.prompt +
              (needs ? "\nWhat the owner says they need most: " + short(needs, 600) : "") +
              "\nThe owner's goal: " +
              short(goal, 600) +
              "\nDeliver the most useful complete first draft with the available facts. Fill noncritical gaps with clearly labeled, reversible assumptions, never invented factual claims. Do not delay the draft for questions.",
          },
        };
        remember({ flow });
      }
      let task;
      for (let i = 0; i < 40 && !task; i++) {
        task = await advanceGoal(flow, (next) => {
          flow = next;
          remember({ flow: next });
        });
        if (!task) await wait(1500);
      }
      if (!task) throw new Error("Your teammate is still being set up. Refresh in a moment.");
      remember({ flow: undefined });
      const latest = (await api<{ data: typeof d }>("/company")).data.employees.find((e) => e.id === host!.id);
      if (latest && latest.state !== "retired")
        await command("employee.lifecycle", { id: latest.id, expectedVersion: latest.version, decision: "retire" });
      onDone();
    });
  }

  const pick = approved && recommendationsForProfile(d.roles, approved)[0]?.role;
  const pickSetup = approved && pick ? setupForProfile(approved, pick) : undefined;
  const researching = !!working && !profile;
  const trailing = (
    <>
      {!confirmed && owner.length >= 3 && !working && <Details facts={facts} onLookUp={(f) => void research(f)} />}
      {confirmed && !profile && !working && (
        <EmployeeSays name="HireZero" label="HireZero picks up research">
          <p>Your details are saved. Shall I read up on you and draft your company brief?</p>
          <div className="co-actions">
            <button
              className="co-primary"
              onClick={() =>
                void research({
                  name: identity!.name,
                  website: identity!.website || "",
                  about: facts.about,
                  goal: identity!.goal,
                })
              }
            >
              Look us up
            </button>
          </div>
        </EmployeeSays>
      )}
      {profile?.state === "draft" && !approved && (
        <Findings
          unread={
            identity?.website && !lookup?.sourceIds?.length
              ? `I couldn’t read ${identity.website.replace(/^https?:\/\//, "").replace(/\/$/, "")}${
                  lookup?.notices?.[0] ? ` (${lookup.notices[0].replace(/\.$/, "")})` : ""
                }, so this starts from what you told me. Add what you can, or check the address.`
              : ""
          }
          profile={profile}
          busy={!!working}
          canRetry={!!lookup?.sourceIds?.length}
          onRewrite={(key, value) => void rewrite(key, value)}
          onAgain={() => void again()}
          onApprove={() => void approve()}
          spent={job?.usage?.billedUsd ?? undefined}
        />
      )}
      {approved && afterApproval.length > 1 && pick && (
        <Offer
          role={pick}
          skill={pickSetup?.firstAssignment.skill}
          business={identity?.name || "your business"}
          cents={snapshot.capabilities.allowanceUnit === "usd-cents"}
          busy={!!working}
          onYes={(terms) => void hire(terms)}
        />
      )}
      {(working || researching) && (
        <EmployeeSays name="HireZero" label="HireZero is working">
          <Working>{working}</Working>
        </EmployeeSays>
      )}
      {error && (
        <p role="alert" className="co-onboard-error">
          {error}
        </p>
      )}
    </>
  );

  return (
    <div className="co-onboard co-desk">
      <header className="co-onboard-head">
        <b>Setting up {identity?.name || facts.name || "your company"}</b>
        <button className="co-ghost" onClick={onUseForm}>
          Use the form instead
        </button>
      </header>
      <CompanyConversation
        key={host.id + ":" + thread.id}
        memberRole={props.memberRole}
        snapshot={snapshot}
        company={false}
        events={props.events}
        employee={host}
        thread={thread.id}
        detail={detail}
        busy={props.busy}
        drafts={props.drafts}
        updateDraft={props.updateDraft}
        run={run}
        refresh={refresh}
        open={() => {}}
        onShowWork={props.onOpenWork}
        createThread={async () => {}}
        leading={greeting}
        trailing={trailing}
      />
    </div>
  );
}

// The offer of the first teammate says what Yes does before it does it: the provider and model, and the most the first
// draft can cost, from the same terms as the classic first-draft form. Yes uses exactly the terms shown.
function Offer({
  role,
  skill,
  business,
  cents,
  busy,
  onYes,
}: {
  role: Role;
  skill?: string;
  business: string;
  cents: boolean;
  busy: boolean;
  onYes: (terms: GoalTerms) => void;
}) {
  const [terms, setTerms] = useState<GoalTerms>();
  const [failed, setFailed] = useState("");
  useEffect(() => {
    if (!skill) return;
    let live = true;
    setTerms(undefined);
    setFailed("");
    api<GoalTerms>("/company/goals/terms?" + new URLSearchParams({ roleId: role.id, skill }))
      .then((next) => live && setTerms(next))
      .catch((e) => live && setFailed((e as Error).message));
    return () => {
      live = false;
    };
  }, [role.id, skill]);
  const provider = providerName(terms?.terms?.provider || "host");
  const limit = spendText(cents, Number(defaultAllowance(cents)));
  return (
    <EmployeeSays name="HireZero" label="HireZero offers your first teammate">
      <p>
        <b>Bring in a {role.onboarding?.name || role.name}?</b> They’ll start on a first draft for {business} straight
        away, and you’ll review it before anything goes anywhere.
      </p>
      {terms ? (
        <>
          <p className="co-onboard-terms">
            Yes writes one private draft using {provider}, {limit}. Nothing is posted, sent or bought, and nothing is
            retried on its own.
          </p>
          <details className="co-goal-run-details">
            <summary>Run details</summary>
            <p>
              {provider} · {terms.route}
            </p>
            <p>
              One private draft · {limit} · {terms.policy.localDeadlineSeconds}s local deadline.
            </p>
          </details>
        </>
      ) : failed ? (
        <p role="alert">I couldn’t check what this draft can cost, so I won’t start it: {failed}</p>
      ) : (
        <p className="co-muted">Checking what this draft can cost…</p>
      )}
      <div className="co-actions">
        <button className="co-primary" disabled={busy || !terms} onClick={() => terms && onYes(terms)}>
          Yes, bring them in
        </button>
      </div>
    </EmployeeSays>
  );
}

// The details HireZero heard, to check before it looks the business up.
function Details({
  facts,
  onLookUp,
}: {
  facts: { name: string; website: string; about: string; goal: string };
  onLookUp: (facts: { name: string; website: string; about: string; goal: string }) => void;
}) {
  const [value, setValue] = useState(() => ({
    name: short(facts.name, 100),
    website: facts.website,
    about: short(facts.about, 600),
    goal: short(facts.goal, 600),
  }));
  const set = (key: keyof typeof value) => (e: { target: { value: string } }) =>
    setValue((v) => ({ ...v, [key]: e.target.value }));
  return (
    <EmployeeSays name="HireZero" label="HireZero checks your details">
      <p>Here’s what I’ve got. Fix anything that’s off, then I’ll look you up.</p>
      <form
        className="co-onboard-details"
        onSubmit={(e) => {
          e.preventDefault();
          onLookUp({ ...value, name: value.name.trim(), goal: value.goal.trim() });
        }}
      >
        <label>
          Business name
          <input required maxLength={100} value={value.name} onChange={set("name")} />
        </label>
        <label>
          Website <small>(optional; I’ll read a few pages)</small>
          <input maxLength={300} value={value.website} placeholder="example.com" onChange={set("website")} />
        </label>
        {!value.website.trim() && (
          <label>
            What you do
            <textarea rows={3} maxLength={600} value={value.about} onChange={set("about")} />
          </label>
        )}
        <label>
          What you want to happen
          <textarea required rows={2} maxLength={600} value={value.goal} onChange={set("goal")} />
        </label>
        <p className="co-muted">
          {value.website.trim()
            ? "Reading your site is free. Writing your brief is one model request within your plan’s limit."
            : "No website? No problem: your brief starts from your words, and you can fill in the rest."}
        </p>
        <div className="co-actions">
          <button className="co-primary" disabled={!value.name.trim() || !value.goal.trim()}>
            Look us up
          </button>
        </div>
      </form>
    </EmployeeSays>
  );
}

// "Here's what I found": each line can be rewritten, the whole draft redone, or approved.
function Findings({
  unread,
  profile,
  busy,
  canRetry,
  onRewrite,
  onAgain,
  onApprove,
  spent,
}: {
  unread: string;
  profile: BusinessProfile;
  busy: boolean;
  canRetry: boolean;
  onRewrite: (key: string, value: string) => void;
  onAgain: () => void;
  onApprove: () => void;
  spent?: number;
}) {
  const [editing, setEditing] = useState("");
  const [text, setText] = useState("");
  const fields = keys.map((key) => profile.fields.find((f) => f.key === key)).filter(Boolean) as ProfileField[];
  return (
    <EmployeeSays name="HireZero" label="HireZero shows what it found">
      {unread ? (
        <p>{unread}</p>
      ) : (
        <p>Here’s what I found. If a line is wrong, rewrite it in your own words; your version always wins.</p>
      )}
      <dl className="co-onboard-found">
        {fields.map((f) => (
          <div key={f.key} className={"co-onboard-fact " + f.verification}>
            <dt>
              {labels[f.key]} <small>{source[f.verification] ?? ""}</small>
            </dt>
            {editing === f.key ? (
              <dd>
                <form
                  onSubmit={(e) => {
                    e.preventDefault();
                    onRewrite(f.key, text);
                    setEditing("");
                  }}
                >
                  <textarea
                    aria-label={"Rewrite " + labels[f.key]}
                    rows={3}
                    maxLength={600}
                    value={text}
                    autoFocus
                    onChange={(e) => setText(e.target.value)}
                  />
                  <div className="co-actions">
                    <button className="co-primary" disabled={busy}>
                      Save
                    </button>
                    <button type="button" onClick={() => setEditing("")}>
                      Cancel
                    </button>
                  </div>
                </form>
              </dd>
            ) : (
              <dd>
                <span>{f.value}</span>
                <button
                  className="co-link"
                  disabled={busy}
                  onClick={() => {
                    setEditing(f.key);
                    setText(f.verification === "unknown" ? "" : f.value);
                  }}
                >
                  {f.verification === "unknown" ? "Add" : "This is wrong"}
                </button>
              </dd>
            )}
          </div>
        ))}
      </dl>
      <div className="co-actions">
        <button className="co-primary" disabled={busy || !!editing} onClick={onApprove}>
          Looks right
        </button>
        {canRetry && (
          <button disabled={busy || !!editing} onClick={onAgain}>
            Try again
          </button>
        )}
      </div>
      {spent != null && <p className="co-muted">Writing this brief cost ${spent.toFixed(4)}.</p>}
    </EmployeeSays>
  );
}
