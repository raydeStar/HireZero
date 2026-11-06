import { useEffect, useRef, useState, type FormEvent } from "react";
import { api } from "../lib/api";
import { LeaveCompany } from "./FreeCompanyActions";

type Member = {
  id: string;
  email: string;
  name: string;
  role: string;
  state: string;
  version: number;
  inviteExpiresAt?: string;
};

// Company members and roles (docs/adr/0020): everyone sees who belongs; owners invite by email, change roles and remove
// members. The server enforces every rule, including that an active owner always remains.
export function Members({
  role,
  version,
  busy,
  run,
  open,
  signupCompany,
  signupInvites,
}: {
  open?: boolean;
  role?: string;
  version: number;
  busy: boolean;
  signupCompany?: boolean;
  signupInvites?: boolean;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
}) {
  const [members, setMembers] = useState<Member[]>([]),
    [email, setEmail] = useState(""),
    [invite, setInvite] = useState("approver"),
    [error, setError] = useState(""),
    [link, setLink] = useState(""),
    [copyMessage, setCopyMessage] = useState(""),
    [inviting, setInviting] = useState(false);
  const sendingInvitation = useRef(false);
  async function inviteFree(address: string, selectedRole: string, expectedVersion: number) {
    if (sendingInvitation.current) return;
    sendingInvitation.current = true;
    setInviting(true);
    setError("");
    setLink("");
    setCopyMessage("");
    try {
      const result = await api<{ member: Member; link: string }>("/company/member-invites", "POST", {
        email: address,
        role: selectedRole,
        expectedVersion,
      });
      setMembers((all) => [...all.filter((member) => member.id !== result.member.id), result.member]);
      setLink(result.link);
      setEmail("");
    } catch (cause) {
      setError(
        (cause as Error).message +
          " Refresh members before explicitly creating a replacement link. No link will be regenerated automatically.",
      );
    } finally {
      sendingInvitation.current = false;
      setInviting(false);
    }
  }
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
      {signupCompany && role !== "owner" && <LeaveCompany />}
      {signupCompany && owner && !signupInvites && (
        <p>New invitations are currently unavailable. Existing members keep their access.</p>
      )}
      {link && (
        <div className="co-invite-link">
          <label>
            Invitation link
            <input readOnly value={link} onFocus={(event) => event.target.select()} />
          </label>
          <button
            onClick={() =>
              void navigator.clipboard
                .writeText(link)
                .then(() => setCopyMessage("Invite link copied."))
                .catch(() => setCopyMessage("Select the link and copy it manually."))
            }
          >
            Copy invite link
          </button>
          <p role="status">
            {copyMessage ||
              "Copy this link now and send it yourself. It expires in seven days. HireZero does not send an email."}
          </p>
        </div>
      )}
      <ul>
        {members
          .filter((m) => m.state !== "revoked")
          .map((m) => (
            <li key={m.id}>
              <strong>{m.name || m.email}</strong> <span className="co-muted">{m.email}</span>{" "}
              {owner && !(signupCompany && (m.role === "owner" || m.state === "invited")) ? (
                <select
                  aria-label={"Role of " + (m.email || m.name)}
                  disabled={busy}
                  value={m.role}
                  onChange={(e) =>
                    void run("member.role", { id: m.id, expectedVersion: m.version, decision: e.target.value })
                  }
                >
                  {!signupCompany && <option value="owner">Owner</option>}
                  <option value="approver">Approver</option>
                  <option value="viewer">Viewer</option>
                </select>
              ) : (
                <span>{m.role}</span>
              )}
              {m.state === "invited" && (
                <span className="co-muted">
                  {m.inviteExpiresAt && Date.parse(m.inviteExpiresAt) <= Date.now() ? " (expired)" : " (invited)"}
                </span>
              )}{" "}
              {owner && signupCompany && signupInvites && m.state === "invited" && (
                <button disabled={busy || inviting} onClick={() => void inviteFree(m.email, m.role, m.version)}>
                  Replace invite link
                </button>
              )}
              {owner && !(signupCompany && m.role === "owner") && (
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
      {owner && (!signupCompany || signupInvites) && (
        <form
          onSubmit={(e: FormEvent) => {
            e.preventDefault();
            const submitted = email;
            if (signupCompany) {
              const prior = members.find((member) => member.id === "invite:" + submitted.trim().toLowerCase());
              void inviteFree(submitted, invite, prior?.version ?? 0);
              return;
            }
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
            {!signupCompany && <option value="owner">Owner</option>}
          </select>
          <button disabled={busy || inviting || !email} type="submit">
            Invite
          </button>
          {signupCompany && (
            <p className="co-muted">
              Up to five pending invitations. All members share this company’s existing allowance.
            </p>
          )}
        </form>
      )}
      {role === "viewer" && (
        <p className="co-muted">You can read everything here; changes need an approver or owner.</p>
      )}
    </details>
  );
}
