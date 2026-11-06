import { useEffect, useRef, useState } from "react";
import { api, company, savePreference } from "../lib/api";

export function JoinedCompanyNotice({ role }: { role?: string }) {
  const [name, setName] = useState("");
  useEffect(() => {
    const query = new URLSearchParams(location.search);
    if (query.get("joined") !== "1") return;
    void api<{ id: string; name: string }[]>("/companies")
      .then((items) => {
        setName(items.find((item) => item.id === company())?.name || "");
        query.delete("joined");
        history.replaceState(null, "", "/?" + query);
      })
      .catch(() => {});
  }, []);
  return name ? (
    <section className="co-joined-notice" aria-label="Company joined">
      <p role="status">
        You joined <strong>{name}</strong> as {role === "approver" ? "an" : "a"} {role}.
      </p>
      {role !== "owner" && <LeaveCompany />}
      <button onClick={() => setName("")}>Dismiss</button>
    </section>
  ) : null;
}

export function CreateFreeCompany() {
  const [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const sending = useRef(false);
  async function create() {
    if (sending.current) return;
    sending.current = true;
    setBusy(true);
    try {
      const created = await api<{ id: string }>("/signup", "POST", {});
      savePreference("company", created.id);
      location.assign("/?company=" + encodeURIComponent(created.id));
    } catch (cause) {
      setError((cause as Error).message + " Refresh your companies to check whether creation completed.");
    }
  }
  return (
    <div className="co-free-company">
      <p>You can also create your own free company. Joining a team does not use your one-company allowance.</p>
      <button disabled={busy} onClick={() => void create()}>
        Create my company
      </button>
      {error && <p role="alert">{error}</p>}
    </div>
  );
}

export function LeaveCompany() {
  const [confirming, setConfirming] = useState(false),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const sending = useRef(false);
  async function leave() {
    if (sending.current) return;
    sending.current = true;
    setBusy(true);
    setError("");
    try {
      const session = await api<{ login: string }>("/session");
      const members = await api<{ id: string; version: number }[]>("/company/members");
      const member = members.find((item) => item.id === session.login);
      if (!member) throw new Error("Your membership could not be confirmed. Refresh access.");
      await api("/company/commands", "POST", {
        requestId: crypto.randomUUID(),
        action: "member.leave",
        expectedVersion: member.version,
      }).catch(() => undefined);
      const companies = await api<{ id: string }[]>("/companies");
      if (companies.some((item) => item.id === company()))
        throw new Error("Leaving was not confirmed. Refresh access before trying again.");
      savePreference("company", "");
      location.assign("/");
    } catch (cause) {
      setError((cause as Error).message);
      sending.current = false;
      setBusy(false);
    }
  }
  return (
    <div className="co-leave-company">
      {confirming ? (
        <>
          <p>You’ll lose access to this company. Existing work and audit records remain.</p>
          <button disabled={busy} onClick={() => void leave()}>
            {busy ? "Leaving…" : "Confirm leave"}
          </button>
          <button disabled={busy} onClick={() => setConfirming(false)}>
            Stay
          </button>
        </>
      ) : (
        <button onClick={() => setConfirming(true)}>Leave company</button>
      )}
      {error && <p role="alert">{error}</p>}
    </div>
  );
}
