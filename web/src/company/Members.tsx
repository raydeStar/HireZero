import { useEffect, useState, type FormEvent } from "react";
import { api } from "../lib/api";

type Member = { id: string; email: string; name: string; role: string; state: string; version: number };

// Company members and roles (docs/adr/0020): everyone sees who belongs; owners invite by email, change roles and remove
// members. The server enforces every rule, including that an active owner always remains.
export function Members({
  role,
  version,
  busy,
  run,
  open,
}: {
  open?: boolean;
  role?: string;
  version: number;
  busy: boolean;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
}) {
  const [members, setMembers] = useState<Member[]>([]),
    [email, setEmail] = useState(""),
    [invite, setInvite] = useState("approver"),
    [error, setError] = useState("");
  useEffect(() => {
    api<Member[]>("/company/members")
      .then((result) => {
        setMembers(result);
        setError("");
      })
      .catch((e: Error) => setError(e.message));
  }, [version]);
  const owner = role === "owner";
  return (
    <details className="co-members" open={open}>
      <summary>
        Members ({members.filter((m) => m.state === "active").length})
        {role && role !== "owner" ? ` · you are ${role}` : ""}
      </summary>
      {error && <p role="alert">{error}</p>}
      <ul>
        {members
          .filter((m) => m.state !== "revoked")
          .map((m) => (
            <li key={m.id}>
              <strong>{m.name || m.email}</strong> <span className="co-muted">{m.email}</span>{" "}
              {owner ? (
                <select
                  aria-label={"Role of " + (m.email || m.name)}
                  disabled={busy}
                  value={m.role}
                  onChange={(e) =>
                    void run("member.role", { id: m.id, expectedVersion: m.version, decision: e.target.value })
                  }
                >
                  <option value="owner">Owner</option>
                  <option value="approver">Approver</option>
                  <option value="viewer">Viewer</option>
                </select>
              ) : (
                <span>{m.role}</span>
              )}
              {m.state === "invited" && <span className="co-muted"> (invited)</span>}{" "}
              {owner && (
                <button
                  disabled={busy}
                  type="button"
                  onClick={() => void run("member.revoke", { id: m.id, expectedVersion: m.version })}
                >
                  Remove
                </button>
              )}
            </li>
          ))}
      </ul>
      {owner && (
        <form
          onSubmit={(e: FormEvent) => {
            e.preventDefault();
            const submitted = email;
            void run("member.invite", { body: submitted, decision: invite }).then((result) => {
              if (result) setEmail((current) => (current === submitted ? "" : current));
            });
          }}
        >
          <label>
            Invite by email
            <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} />
          </label>
          <select aria-label="Role for the invitation" value={invite} onChange={(e) => setInvite(e.target.value)}>
            <option value="approver">Approver</option>
            <option value="viewer">Viewer</option>
            <option value="owner">Owner</option>
          </select>
          <button disabled={busy || !email} type="submit">
            Invite
          </button>
        </form>
      )}
      {role === "viewer" && (
        <p className="co-muted">You can read everything here; changes need an approver or owner.</p>
      )}
    </details>
  );
}
