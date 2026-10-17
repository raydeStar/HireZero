import { useCallback, useEffect, useState } from "react";
import { Hourglass } from "lucide-react";
import { api } from "./api";

export type AccessRecord = {
  login: string;
  displayName: string | null;
  status: "pending" | "visitor" | "approved" | "denied" | string;
  firstSeen: string;
  lastSeen: string;
  requestId: string | null;
  role: string | null;
};
const roleLabels: Record<string, string> = {
  visitor: "Visitor · may ask; no agent effort until you allow it",
  trusted: "Trusted · usually a yes; you approve before anything runs",
  boss: "Boss · sanitized log of your company work",
};

/** Shown to someone on the tailnet who isn't let in yet; it checks back on its own. */
export function WaitingForAccess({ login }: { login: string }) {
  useEffect(() => {
    const timer = setInterval(() => {
      void fetch("/api/session", { headers: { Accept: "application/json" } })
        .then((response) => (response.ok ? response.json() : null))
        .then((session) => {
          if (session && session.role !== "pending") location.reload();
        })
        .catch(() => {});
    }, 20000);
    return () => clearInterval(timer);
  }, []);
  return (
    <main className="fe-loading dev-entry">
      <Hourglass size={30} />
      <h1>Waiting to be let in</h1>
      <p>
        You're signed in to the tailnet as <strong>{login}</strong>. The owner has been told you're here and will choose
        what you can do.
      </p>
      <p className="fe-muted">This page checks back on its own and opens as soon as you're approved.</p>
    </main>
  );
}

/** The owner's decision on one person: approve with a role, change the role, or deny. */
export function AccessCard({ record, onChanged }: { record: AccessRecord; onChanged: () => void }) {
  const [role, setRole] = useState(
      record.role && record.role !== "visitor" ? record.role : record.status === "pending" ? "visitor" : "trusted",
    ),
    [busy, setBusy] = useState(""),
    [error, setError] = useState("");
  async function run(kind: string, path: string, body: object) {
    setBusy(kind);
    setError("");
    try {
      await api(path, "POST", body);
      onChanged();
    } catch (cause) {
      setError((cause as Error).message);
    } finally {
      setBusy("");
    }
  }
  const who = record.displayName ? record.displayName + " (" + record.login + ")" : record.login;
  const state =
    record.status === "pending"
      ? "is waiting to be let in"
      : record.status === "visitor"
        ? "joined automatically as a visitor"
        : record.status === "denied"
          ? "was denied"
          : "is in as " + (record.role ?? "a visitor");
  return (
    <div className={"fe-section dev-access " + record.status}>
      <p>
        <strong>{who}</strong> {state}.
      </p>
      {record.status !== "denied" && (
        <div className="dev-shift-actions">
          <label className="dev-check">
            Role
            <select value={role} onChange={(event) => setRole(event.target.value)}>
              {Object.entries(roleLabels).map(([value, text]) => (
                <option key={value} value={value}>
                  {text}
                </option>
              ))}
            </select>
          </label>
          <button
            className="primary"
            disabled={!!busy}
            onClick={() => void run("approve", "/access/approve", { login: record.login, role })}
          >
            {busy === "approve" ? "Saving…" : record.status === "pending" ? "Let them in" : "Change role"}
          </button>
          <button
            className="fe-ghost"
            disabled={!!busy}
            onClick={() => void run("deny", "/access/deny", { login: record.login })}
          >
            {record.status === "pending" ? "Deny" : "Revoke"}
          </button>
        </div>
      )}
      {error && (
        <p className="fe-alert" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}

/** Team page: everyone who opened HireZero without an invitation, newest first. */
export function AccessSection() {
  const [data, setData] = useState<{ settings: { autoVisitorDomains: string[] }; people: AccessRecord[] } | null>(null),
    [domains, setDomains] = useState(""),
    [error, setError] = useState("");
  const load = useCallback(async () => {
    try {
      const value = await api<{ settings: { autoVisitorDomains: string[] }; people: AccessRecord[] }>("/access");
      setData(value);
      setDomains(value.settings.autoVisitorDomains.join(", "));
    } catch (cause) {
      setError((cause as Error).message);
    }
  }, []);
  useEffect(() => {
    void load();
    const timer = setInterval(() => {
      if (document.visibilityState === "visible") void load();
    }, 30000);
    return () => clearInterval(timer);
  }, [load]);
  async function saveDomains() {
    try {
      await api("/access/settings", "PUT", {
        autoVisitorDomains: domains
          .split(",")
          .map((value) => value.trim())
          .filter(Boolean),
      });
      await load();
    } catch (cause) {
      setError((cause as Error).message);
    }
  }
  const waiting = data?.people.filter((person) => person.status === "pending") ?? [];
  return (
    <section className="fe-section fe-stack">
      <h2>People who knocked {waiting.length > 0 && <span className="fe-count">{waiting.length} waiting</span>}</h2>
      <p className="fe-muted">
        Anyone on your tailnet who opens HireZero without an invitation shows up here and in a conversation. People from
        these domains come in as visitors automatically; everyone else waits for you.
      </p>
      <div className="dev-shift-actions">
        <label className="dev-check">
          Auto-visitor domains
          <input
            value={domains}
            maxLength={500}
            onChange={(event) => setDomains(event.target.value)}
            placeholder="example.invalid"
          />
        </label>
        <button className="fe-ghost" onClick={() => void saveDomains()}>
          Save
        </button>
      </div>
      {!data ? (
        <p className="fe-muted" role="status">
          Loading…
        </p>
      ) : data.people.length ? (
        data.people.map((person) => <AccessCard key={person.login} record={person} onChanged={() => void load()} />)
      ) : (
        <p className="fe-muted">Nobody yet.</p>
      )}
      {error && (
        <p className="fe-alert" role="alert">
          {error}
        </p>
      )}
    </section>
  );
}
