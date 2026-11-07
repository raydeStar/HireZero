import { useEffect, useState } from "react";
import { api } from "../lib/api";
import type { Snapshot } from "./api";
import { approvedBusinessProfile } from "../roles/company";

type Me = { id: string; name: string; email: string; version: number; about?: string | null };

// Who employees are talking with (docs/adr/0185): "About you" is each person's own, so two people in one company are
// two different people to their employees; "About the company" is the business brief everyone shares.
export function AboutYou({
  snapshot,
  employeeName,
  run,
}: {
  snapshot: Snapshot;
  employeeName: string;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
}) {
  const [me, setMe] = useState<Me | null>(null);
  const [text, setText] = useState("");
  const [dirty, setDirty] = useState(false);
  const [saved, setSaved] = useState("");
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    let live = true;
    Promise.all([api<{ login?: string | null }>("/session"), api<Me[]>("/company/members")])
      .then(([session, members]) => {
        const mine = members.find((m) => m.id === session.login);
        if (!live || !mine) return;
        setMe(mine);
        if (!dirty) setText(mine.about ?? "");
      })
      .catch(() => {});
    return () => {
      live = false;
    };
  }, [snapshot.data.cursor]);
  const profile = approvedBusinessProfile(snapshot);
  const fields = (profile?.fields ?? []).filter((f) => f.value.trim() && f.verification !== "unknown").slice(0, 3);
  async function save() {
    if (!me) return;
    setBusy(true);
    try {
      const result = (await run("member.about", { expectedVersion: me.version, body: text })) as Me | undefined;
      if (result) {
        setMe(result);
        setDirty(false);
        setSaved(text.trim() ? `Saved. ${employeeName} uses it from their next reply.` : "Cleared.");
      }
    } finally {
      setBusy(false);
    }
  }
  return (
    <>
      {me && (
        <section className="co-about-you" aria-label="About you">
          <h4>About you</h4>
          <p className="co-muted">
            What {employeeName} and the rest of the team should know about you{me.name ? `, ${me.name}` : ""}: what to
            call you, your part in the business and how you like updates. It’s yours alone; everyone else here has their
            own, and members of this company can read it.
          </p>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              void save();
            }}
          >
            <label>
              <span className="co-hidden-label">About you</span>
              <textarea
                rows={4}
                maxLength={2000}
                value={text}
                placeholder="Call me Sam. I run the shop on weekday mornings and like short, direct updates."
                disabled={busy}
                onChange={(e) => {
                  setText(e.target.value);
                  setDirty(true);
                  setSaved("");
                }}
              />
            </label>
            <div className="co-actions">
              <button className="co-primary" type="submit" disabled={busy || !dirty}>
                Save about you
              </button>
            </div>
            {saved && (
              <p role="status" className="co-muted">
                {saved}
              </p>
            )}
          </form>
        </section>
      )}
      <section className="co-about-company" aria-label="About the company">
        <h4>About the company</h4>
        {snapshot.data.business?.name || fields.length ? (
          <>
            <p>
              <b>{snapshot.data.business?.name}</b>
            </p>
            {fields.map((f) => (
              <p key={f.key} className="co-muted">
                {f.value.length > 220 ? f.value.slice(0, f.value.lastIndexOf(" ", 217)) + "…" : f.value}
              </p>
            ))}
          </>
        ) : (
          <p className="co-muted">Not described yet.</p>
        )}
        <p className="co-muted">
          Everyone here shares this, and every employee works from it. Owners change it in Company knowledge → Business
          brief.
        </p>
      </section>
    </>
  );
}
