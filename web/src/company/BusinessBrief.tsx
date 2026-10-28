import { memberStorage as sessionStorage } from "../lib/api";
import { useRef, useState } from "react";
import { api, company } from "../lib/api";
import {
  command,
  type BusinessIdentity,
  type BusinessProfile,
  type BusinessSource,
  type ProfileField,
  type Snapshot,
  type Employee,
  type Work,
} from "./api";
import "./business.css";
import { BusinessDrafting } from "./BusinessDrafting";
import { BusinessLookup } from "./BusinessLookup";
import { BusinessFreshness } from "./BusinessFreshness";
import { BusinessFieldEvidence, BusinessHistory } from "./BusinessHistory";
import { TeamRecommendations } from "./TeamRecommendations";

const labels: Record<string, string> = {
  business: "What the business does",
  services: "Products and services",
  customers: "Who you serve",
  voice: "Brand voice",
  market: "Market and location",
  competitors: "Competitors",
  opportunities: "Ideas to investigate",
  goals: "What you want to achieve",
  boundaries: "Boundaries and claims to avoid",
};
type IdentityInput = { name: string; website: string; goal: string; location: string; industry: string };
type Draft = {
  input: IdentityInput;
  identityVersion: number;
  fields: ProfileField[];
  profileId?: string;
  profileVersion: number;
  businessVersion: number;
};
const storageKey = "hirezero-business-draft-v1:" + (company() || "local-company");
function fieldsFor(identity?: BusinessIdentity | null): ProfileField[] {
  return Object.keys(labels).map((key) => ({
    key,
    value: key === "business" ? identity?.name || "Unknown" : key === "goals" ? identity?.goal || "Unknown" : "Unknown",
    verification: identity && ["business", "goals"].includes(key) ? "owner" : "unknown",
    citations: [],
  }));
}
function currentProfile(snapshot: Snapshot) {
  return snapshot.data.businessProfiles
    ?.filter((p) => p.businessVersion === snapshot.data.business?.version && ["draft", "approved"].includes(p.state))
    .at(-1);
}
function fresh(snapshot: Snapshot): Draft {
  const identity = snapshot.data.business,
    profile = currentProfile(snapshot);
  return {
    input: {
      name: identity?.name || "",
      website: identity?.website || "",
      goal: identity?.goal || "",
      location: identity?.location || "",
      industry: identity?.industry || "",
    },
    identityVersion: identity?.version || 0,
    fields: profile?.fields || fieldsFor(identity),
    profileId: profile?.state === "draft" ? profile.id : undefined,
    profileVersion: profile?.state === "draft" ? profile.version : 0,
    businessVersion: identity?.version || 0,
  };
}
function initial(snapshot: Snapshot): Draft {
  try {
    const draft = JSON.parse(sessionStorage.getItem(storageKey) || "null") as Draft | null;
    if (
      draft &&
      typeof draft.input?.name === "string" &&
      Array.isArray(draft.fields) &&
      draft.fields.length === 9 &&
      draft.fields.every((f) => typeof f.value === "string" && labels[f.key] && Array.isArray(f.citations))
    )
      return draft;
  } catch {
    /* The server keeps saved records even when this browser cannot keep an unfinished draft. */
  }
  return fresh(snapshot);
}

export function BusinessBrief({
  snapshot,
  refresh,
  onHire,
  onEmployee,
  onWork,
  role,
}: {
  snapshot: Snapshot;
  refresh: () => Promise<void>;
  onHire: (role?: string) => void;
  onEmployee: (employee: Employee) => void;
  onWork: (task: Work) => void;
  role?: string;
}) {
  const [identity, setIdentity] = useState(snapshot.data.business);
  const [profile, setProfile] = useState(currentProfile(snapshot));
  const [approved, setApproved] = useState(snapshot.data.businessProfiles?.find((p) => p.state === "approved"));
  const [draft, setDraft] = useState(() => initial(snapshot));
  const [sources, setSources] = useState(snapshot.data.businessSources || []);
  const [editingIdentity, setEditingIdentity] = useState(!identity);
  const [editingProfile, setEditingProfile] = useState(
    !profile ||
      draft.businessVersion !== identity?.version ||
      JSON.stringify(draft.fields) !== JSON.stringify(profile.fields),
  );
  const latestDraft = snapshot.data.businessDrafts?.filter((d) => d.businessVersion === identity?.version).at(-1);
  const hasActiveDraft = !!latestDraft && ["proposed", "running", "unknown"].includes(latestDraft.state);
  const [researchOpen, setResearchOpen] = useState(!profile || hasActiveDraft);
  const [sourceUrl, setSourceUrl] = useState(identity?.website || "");
  const [source, setSource] = useState<BusinessSource | null>(null);
  const [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [notice, setNotice] = useState("");
  const locked = useRef(false);
  const approvedReview = useRef<HTMLDetailsElement>(null);
  const owner = !role || role === "owner";
  const confirmed = identity?.state === "confirmed";
  const currentSources = sources.filter((s) => s.businessVersion === identity?.version);
  const ready =
    profile?.state === "draft" &&
    profile.version === draft.profileVersion &&
    profile.id === draft.profileId &&
    JSON.stringify(profile.fields) === JSON.stringify(draft.fields);
  const changedElsewhere = (snapshot.data.business?.version || 0) > (identity?.version || 0);
  async function refreshEvidence() {
    const saved = await api<Snapshot>("/company");
    setSources(saved.data.businessSources || []);
    await refresh();
  }
  function change(next: Draft) {
    setDraft(next);
    try {
      sessionStorage.setItem(storageKey, JSON.stringify(next));
    } catch {
      setNotice("This browser cannot keep unfinished edits after closing. Save your draft before leaving.");
    }
  }
  async function perform(work: () => Promise<void>, message: string, refreshAfter = true) {
    if (locked.current) return;
    locked.current = true;
    setBusy(true);
    setError("");
    setNotice("");
    try {
      await work();
      setNotice(message);
      if (refreshAfter)
        try {
          await refresh();
        } catch {
          setNotice(
            message + " The overview could not refresh; your saved change is kept. Reload saved state to check it.",
          );
        }
    } catch (e) {
      setError((e as Error).message);
    } finally {
      locked.current = false;
      setBusy(false);
    }
  }
  async function reload() {
    await perform(async () => {
      const saved = await api<Snapshot>("/company");
      setIdentity(saved.data.business);
      setProfile(currentProfile(saved));
      setApproved(saved.data.businessProfiles?.find((p) => p.state === "approved"));
      setSources(saved.data.businessSources || []);
      change(fresh(saved));
      setEditingIdentity(!saved.data.business);
      setEditingProfile(!currentProfile(saved));
      setResearchOpen(
        !currentProfile(saved) ||
          ["proposed", "running", "unknown"].includes(
            saved.data.businessDrafts?.filter((d) => d.businessVersion === saved.data.business?.version).at(-1)
              ?.state || "",
          ),
      );
      setSource(null);
      setSourceUrl(saved.data.business?.website || "");
    }, "Saved state loaded. The form now matches your company records.");
  }
  function editField(key: string, patch: Partial<ProfileField>) {
    change({ ...draft, fields: draft.fields.map((f) => (f.key === key ? { ...f, ...patch } : f)) });
  }
  return (
    <section className="co-business" aria-label="Business brief">
      <div className="co-business-intro">
        <p className="co-eyebrow">Your business, understood first</p>
        <h2>Give your team a shared starting point.</h2>
        <p>Confirm the right business, review the facts, then choose an employee. Unknown details can stay unknown.</p>
      </div>
      <ol className="co-business-steps">
        <li aria-current={!confirmed ? "step" : undefined}>1. Confirm business</li>
        <li aria-current={confirmed && profile?.state !== "approved" ? "step" : undefined}>2. Review brief</li>
        <li aria-current={profile?.state === "approved" ? "step" : undefined}>3. Set up your team</li>
      </ol>
      {confirmed && !editingIdentity && profile?.state === "approved" && !editingProfile && (
        <TeamRecommendations
          snapshot={snapshot}
          disabled={busy || !owner}
          onHire={onHire}
          onEmployee={onEmployee}
          onWork={onWork}
        />
      )}
      {!owner && (
        <p role="status">
          Only the company owner can change or approve this brief. You can read the saved facts and sources.
        </p>
      )}
      {error && (
        <p className="co-error" role="alert">
          {error} Your form is kept.
        </p>
      )}
      {notice && <p role="status">{notice}</p>}
      {changedElsewhere && (
        <p role="status">The business changed in another view. Reload saved state before continuing.</p>
      )}
      <details className="co-business-recovery">
        <summary>Check saved state or recover a lost reply</summary>
        <p>
          Loading replaces this form with the saved business and brief. Copy any unfinished edits you want to keep
          first.
        </p>
        <button disabled={busy} onClick={reload}>
          Reload saved state
        </button>
      </details>
      {approved && (
        <BusinessFreshness
          key={approved.id}
          profile={approved}
          owner={owner}
          labels={labels}
          sources={sources}
          refresh={refreshEvidence}
          onCorrect={() => {
            setEditingProfile(true);
            requestAnimationFrame(() =>
              document.querySelector(".co-business-profile")?.scrollIntoView({ behavior: "smooth", block: "start" }),
            );
          }}
        />
      )}
      {approved && owner && (
        <details className="co-business-recovery" ref={approvedReview}>
          <summary>Replace or withdraw the approved brief</summary>
          <p>
            This exact version is currently available to your employees. Revoking it stops its use for new work.
            Existing task history is kept. Review it here before withdrawing it; then you can approve a replacement or
            change the business identity.
          </p>
          <dl>
            {approved.fields.map((field) => (
              <div key={field.key}>
                <dt>{labels[field.key]}</dt>
                <dd>
                  {field.value} ({field.verification})
                </dd>
              </div>
            ))}
          </dl>
          <button
            disabled={busy}
            onClick={() =>
              perform(async () => {
                await command(
                  "business.profile.review",
                  { id: approved.id, expectedVersion: approved.version, digest: approved.digest, decision: "revoke" },
                  () => {},
                );
                setApproved(undefined);
                if (profile?.id === approved.id) {
                  setProfile(undefined);
                  setEditingProfile(true);
                  change({ ...draft, profileId: undefined, profileVersion: 0 });
                }
              }, "The selected brief was revoked. Review and approve a new brief before your team uses it.")
            }
          >
            Revoke this approved brief
          </button>
        </details>
      )}
      {editingIdentity ? (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void perform(async () => {
              const saved = (await command(
                "business.identify",
                {
                  expectedVersion: draft.identityVersion,
                  body: JSON.stringify({ ...draft.input, website: draft.input.website || null }),
                },
                () => {},
              )) as BusinessIdentity;
              setIdentity(saved);
              setProfile(undefined);
              setSource(null);
              setSourceUrl(saved.website || "");
              setEditingIdentity(false);
              setEditingProfile(true);
              change({
                ...draft,
                identityVersion: saved.version,
                businessVersion: saved.version,
                fields: fieldsFor(saved),
                profileId: undefined,
                profileVersion: 0,
              });
            }, "Business saved for confirmation.");
          }}
        >
          <fieldset disabled={busy || !owner}>
            <legend>Find the right business</legend>
            <label>
              Business name
              <input
                required
                maxLength={100}
                value={draft.input.name}
                onChange={(e) => change({ ...draft, input: { ...draft.input, name: e.target.value } })}
              />
            </label>
            <label>
              Website (optional)
              <input
                type="url"
                placeholder="https://example.com"
                maxLength={800}
                value={draft.input.website}
                onChange={(e) => change({ ...draft, input: { ...draft.input, website: e.target.value } })}
              />
            </label>
            <div className="co-business-grid">
              <label>
                Location (optional)
                <input
                  maxLength={200}
                  value={draft.input.location}
                  onChange={(e) => change({ ...draft, input: { ...draft.input, location: e.target.value } })}
                />
              </label>
              <label>
                Industry (optional)
                <input
                  maxLength={200}
                  value={draft.input.industry}
                  onChange={(e) => change({ ...draft, input: { ...draft.input, industry: e.target.value } })}
                />
              </label>
            </div>
            <label>
              What would you like help achieving?
              <textarea
                required
                maxLength={1000}
                rows={3}
                value={draft.input.goal}
                onChange={(e) => change({ ...draft, input: { ...draft.input, goal: e.target.value } })}
              />
            </label>
            <p className="co-muted">
              Website research starts only after you confirm this identity. Without a website, you can write the brief
              from your own knowledge.
            </p>
            <button className="co-primary" type="submit">
              {busy ? "Saving…" : "Review business identity"}
            </button>
            {identity && (
              <button
                type="button"
                onClick={() => {
                  setEditingIdentity(false);
                  setError("");
                }}
              >
                Cancel identity edit
              </button>
            )}
          </fieldset>
        </form>
      ) : (
        identity && (
          <article className="co-business-identity">
            <h3>{identity.name}</h3>
            <p>
              {identity.website ? (
                <a href={identity.website} target="_blank" rel="noreferrer">
                  {identity.website}
                </a>
              ) : (
                "No website added"
              )}
            </p>
            <p>{[identity.location, identity.industry].filter(Boolean).join(" · ")}</p>
            <p>{identity.goal}</p>
            <p>
              {confirmed ? "Business identity confirmed" : "Check the name, website and location before confirming."}
            </p>
            <div className="co-actions">
              {!confirmed && (
                <button
                  disabled={busy || !owner}
                  className="co-primary"
                  onClick={() =>
                    perform(async () => {
                      const saved = (await command(
                        "business.confirm",
                        { expectedVersion: identity.version, digest: identity.digest },
                        () => {},
                      )) as BusinessIdentity;
                      setIdentity(saved);
                      change({ ...draft, identityVersion: saved.version, businessVersion: saved.version });
                    }, "Business confirmed. You can now collect evidence and review your brief.")
                  }
                >
                  Confirm this is my business
                </button>
              )}
              <button
                disabled={busy || !owner}
                onClick={() => {
                  setEditingIdentity(true);
                  change({
                    ...draft,
                    input: {
                      name: identity.name,
                      website: identity.website || "",
                      goal: identity.goal,
                      location: identity.location,
                      industry: identity.industry,
                    },
                    identityVersion: identity.version,
                  });
                }}
              >
                Edit business identity
              </button>
            </div>
          </article>
        )
      )}
      {identity && !confirmed && !editingIdentity && owner && (
        <details className="co-business-search" open={!identity.website}>
          <summary>{identity.website ? "Find a different website" : "Find a business website"}</summary>
          <BusinessLookup
            key={"search:" + identity.version}
            snapshot={snapshot}
            identity={identity}
            kind="search"
            refresh={refresh}
            choose={(url) => {
              setEditingIdentity(true);
              change({
                ...draft,
                identityVersion: identity.version,
                input: {
                  name: identity.name,
                  website: url,
                  goal: identity.goal,
                  location: identity.location,
                  industry: identity.industry,
                },
              });
              setNotice(
                "Possible website selected. Review the business details, then confirm the identity before importing pages.",
              );
            }}
          />
        </details>
      )}
      {confirmed && !editingIdentity && (
        <>
          <details
            className="co-business-research"
            open={researchOpen}
            onToggle={(event) => setResearchOpen(event.currentTarget.open)}
          >
            <summary>Website sources and draft requests</summary>
            <details className="co-business-sources" open={editingProfile}>
              <summary>Evidence from your website</summary>
              <p>
                Discover a small set of useful pages, or add a particular page yourself. Every saved source keeps its
                address and retrieval date.
              </p>
              {identity?.website && owner && (
                <BusinessLookup
                  key={"discovery:" + identity.version}
                  snapshot={snapshot}
                  identity={identity}
                  kind="discovery"
                  refresh={refreshEvidence}
                />
              )}
              {identity?.website && owner && (
                <form
                  onSubmit={(e) => {
                    e.preventDefault();
                    void perform(async () => {
                      const saved = await api<BusinessSource>("/company/business/research", "POST", {
                        businessVersion: identity.version,
                        digest: identity.digest,
                        url: sourceUrl,
                      });
                      setSource(saved);
                      setSources((old) => [...old.filter((s) => s.id !== saved.id), saved]);
                    }, "Source saved. Review the excerpt before using it in the brief.");
                  }}
                >
                  <label>
                    Page address
                    <input
                      required
                      type="url"
                      maxLength={800}
                      value={sourceUrl}
                      disabled={busy}
                      onChange={(e) => setSourceUrl(e.target.value)}
                    />
                  </label>
                  <button disabled={busy} type="submit">
                    {busy ? "Working…" : "Read public page"}
                  </button>
                </form>
              )}
              {currentSources.length === 0 && (
                <p className="co-muted">No sources saved yet. You can still enter owner-provided facts below.</p>
              )}
              <ul>
                {currentSources.map((s) => (
                  <li key={s.id}>
                    <button
                      disabled={busy}
                      onClick={() =>
                        perform(
                          async () =>
                            setSource(
                              await api<BusinessSource>("/company/business/sources/" + encodeURIComponent(s.id)),
                            ),
                          "Source opened.",
                          false,
                        )
                      }
                    >
                      {s.title || s.url}
                    </button>
                    <small>
                      Retrieved {new Date(s.retrievedAt).toLocaleString()}
                      {s.clipped ? " · excerpt only" : ""}
                    </small>
                  </li>
                ))}
              </ul>
              {source && (
                <details open className="co-business-excerpt">
                  <summary>{source.title || "Source excerpt"}</summary>
                  <a href={source.url} target="_blank" rel="noreferrer">
                    Open original page
                  </a>
                  <pre>{source.text}</pre>
                </details>
              )}
            </details>
            {owner && identity && currentSources.length > 0 && (
              <BusinessDrafting
                snapshot={snapshot}
                identity={identity}
                sources={currentSources}
                refresh={refresh}
                load={(generated) => {
                  const baseline = profile?.fields || fieldsFor(identity);
                  const fields = generated.fields.map((field) => {
                    const local = draft.fields.find((f) => f.key === field.key);
                    return local && JSON.stringify(local) !== JSON.stringify(baseline.find((f) => f.key === field.key))
                      ? local
                      : field;
                  });
                  setProfile(generated);
                  setEditingProfile(JSON.stringify(fields) !== JSON.stringify(generated.fields));
                  setResearchOpen(false);
                  change({
                    ...draft,
                    fields,
                    profileId: generated.id,
                    profileVersion: generated.version,
                    businessVersion: generated.businessVersion,
                  });
                  setNotice(
                    "Generated brief loaded. Your unsaved field edits are kept. Review and save any edits before approval.",
                  );
                }}
              />
            )}
          </details>
          <section className="co-business-profile">
            <h3>
              {profile?.state === "approved" && !editingProfile
                ? "Approved business brief"
                : "Review your business brief"}
            </h3>
            <p>
              {editingProfile
                ? "Use your own facts, or attach an exact quote for a sourced claim. Ideas remain hypotheses. Saving a draft does not make it available to employees."
                : profile?.state === "approved"
                  ? "Your employees can use this approved version. Open the evidence to check a source or quotation."
                  : "Read the facts and hypotheses below, then approve or edit them. This saved draft is not available to employees yet."}
            </p>
            {!editingProfile && owner && (
              <button
                disabled={busy}
                onClick={() => {
                  setEditingProfile(true);
                  change({
                    ...draft,
                    profileId: profile?.state === "draft" ? profile.id : undefined,
                    profileVersion: profile?.state === "draft" ? profile.version : 0,
                  });
                }}
              >
                {profile?.state === "draft" ? "Edit brief" : "Edit a new brief version"}
              </button>
            )}
            {(!editingProfile || !owner) && (
              <dl className="co-business-approved co-business-readonly" aria-label="Saved business brief">
                {(profile?.fields || draft.fields).map((field) => (
                  <div key={field.key}>
                    <dt>{labels[field.key]}</dt>
                    <dd>
                      {field.value}
                      <BusinessFieldEvidence field={field} sources={sources} collapsible />
                    </dd>
                  </div>
                ))}
              </dl>
            )}
            <fieldset hidden={!editingProfile || !owner} disabled={busy || !owner || !editingProfile}>
              <legend>Shared company facts</legend>
              {draft.fields.map((field) => (
                <div className="co-business-field" key={field.key}>
                  <label>
                    {labels[field.key]}
                    <textarea
                      maxLength={600}
                      rows={2}
                      value={field.value}
                      onChange={(e) =>
                        editField(field.key, { value: e.target.value, verification: "owner", citations: [] })
                      }
                    />
                  </label>
                  <label>
                    Evidence for {labels[field.key]?.toLowerCase()}
                    <select
                      value={field.verification}
                      onChange={(e) => {
                        const verification = e.target.value as ProfileField["verification"];
                        editField(field.key, {
                          verification,
                          value: verification === "unknown" ? "Unknown" : field.value,
                          citations: ["owner", "unknown"].includes(verification) ? [] : field.citations,
                        });
                      }}
                    >
                      <option value="owner">Provided or corrected by me</option>
                      <option value="sourced">Supported by a source</option>
                      <option value="hypothesis">Hypothesis to investigate</option>
                      <option value="unknown">Unknown / omit this detail</option>
                    </select>
                  </label>
                  {["sourced", "hypothesis"].includes(field.verification) && (
                    <div className="co-business-citation">
                      <label>
                        Source for {labels[field.key]?.toLowerCase()}
                        <select
                          value={field.citations[0]?.sourceId || ""}
                          onChange={(e) =>
                            editField(field.key, { citations: [{ sourceId: e.target.value, quote: "" }] })
                          }
                        >
                          <option value="">Choose saved evidence</option>
                          {currentSources.map((s) => (
                            <option key={s.id} value={s.id}>
                              {s.title || s.url}
                            </option>
                          ))}
                        </select>
                      </label>
                      <label>
                        Exact supporting quote
                        <textarea
                          rows={2}
                          maxLength={600}
                          value={field.citations[0]?.quote || ""}
                          onChange={(e) =>
                            editField(field.key, {
                              citations: [{ sourceId: field.citations[0]?.sourceId || "", quote: e.target.value }],
                            })
                          }
                        />
                      </label>
                    </div>
                  )}
                </div>
              ))}
            </fieldset>
            {approved && profile?.state === "draft" && (
              <div className="co-business-recovery" role="note">
                <p id="business-replacement-guidance">
                  Your team still uses the previously approved brief. To use this draft instead, review and withdraw
                  that version first. Existing task history is kept.
                </p>
                {owner && (
                  <button
                    disabled={busy}
                    onClick={() => {
                      const review = approvedReview.current;
                      if (!review) return;
                      review.open = true;
                      review.scrollIntoView({ behavior: "smooth", block: "start" });
                      review.querySelector("summary")?.focus();
                    }}
                  >
                    Review the currently approved brief
                  </button>
                )}
              </div>
            )}
            {owner && (
              <div className="co-actions">
                {editingProfile ? (
                  <>
                    <button
                      disabled={busy || draft.businessVersion !== identity?.version}
                      onClick={() =>
                        perform(async () => {
                          const saved = (await command(
                            "business.profile.save",
                            {
                              id: draft.profileId,
                              expectedVersion: draft.profileVersion,
                              body: JSON.stringify({ businessVersion: identity?.version, fields: draft.fields }),
                            },
                            () => {},
                          )) as BusinessProfile;
                          setProfile(saved);
                          setEditingProfile(false);
                          setResearchOpen(hasActiveDraft);
                          change({
                            ...draft,
                            fields: saved.fields,
                            profileId: saved.id,
                            profileVersion: saved.version,
                          });
                        }, "Brief draft saved. Review it, then approve the facts your employees may use.")
                      }
                    >
                      Save brief draft
                    </button>
                  </>
                ) : null}
                {profile?.state === "draft" && (
                  <button
                    className="co-primary"
                    disabled={busy || !ready || !!approved}
                    aria-describedby={approved ? "business-replacement-guidance" : undefined}
                    onClick={() =>
                      perform(async () => {
                        const saved = (await command(
                          "business.profile.review",
                          {
                            id: profile!.id,
                            expectedVersion: profile!.version,
                            digest: profile!.digest,
                            decision: "approve",
                          },
                          () => {},
                        )) as BusinessProfile;
                        setProfile(saved);
                        setApproved(saved);
                        setEditingProfile(false);
                        change({ ...draft, profileId: undefined, profileVersion: 0 });
                      }, "Business brief approved. Your employees can now use these shared facts.")
                    }
                  >
                    Approve brief for my team
                  </button>
                )}
              </div>
            )}
          </section>
          <BusinessHistory revision={`${profile?.id}:${profile?.version}`} labels={labels} sources={sources} />
        </>
      )}
    </section>
  );
}
