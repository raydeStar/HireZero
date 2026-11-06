import { useEffect, useRef, useState } from "react";
import { api, bindMemberSession, savePreference } from "../lib/api";

type Invitation = {
  companyId: string;
  companyName: string;
  role: string;
  version: number;
  invitation: string;
  signedIn: boolean;
  browserScope?: string;
  accountEmail?: string;
  matches: boolean;
};

export function JoinCompany() {
  const [invitation, setInvitation] = useState<Invitation>(),
    [error, setError] = useState(""),
    [joining, setJoining] = useState(false);
  const generation = useRef(0),
    sending = useRef(false);
  useEffect(() => {
    function load() {
      const current = ++generation.current;
      setInvitation(undefined);
      setError("");
      setJoining(sending.current);
      // The fragment never reaches the host, proxy, or sign-in provider. No secrets in the guest book.
      const token = location.hash.slice(1);
      history.replaceState(null, "", "/join");
      void (async () => {
        if (token) await api("/join/prepare", "POST", { token });
        const preview = await api<Invitation>("/join");
        if (current !== generation.current) return;
        if (preview.signedIn) bindMemberSession("hosted", preview.browserScope);
        setInvitation(preview);
      })().catch((cause: Error) => {
        if (current === generation.current) setError(cause.message);
      });
    }
    load();
    window.addEventListener("hashchange", load);
    return () => {
      generation.current++;
      window.removeEventListener("hashchange", load);
    };
  }, []);
  async function join() {
    if (!invitation?.matches || sending.current) return;
    const current = generation.current;
    sending.current = true;
    setJoining(true);
    try {
      const joined = await api<{ id: string }>("/join/accept", "POST", {
        invitation: invitation.invitation,
        expectedVersion: invitation.version,
      });
      if (current !== generation.current) return;
      savePreference("company", joined.id);
      location.assign("/?company=" + encodeURIComponent(joined.id) + "&joined=1");
    } catch (cause) {
      if (current !== generation.current) return;
      setError(
        (cause as Error).message +
          " Check your companies to see whether the join completed. No request will be repeated automatically.",
      );
      // An uncertain acceptance is resolved by reading memberships, never by an automatic second POST.
    } finally {
      // A late reply belongs to the review that sent it. No usher may replace the guest's new invitation.
      if (current !== generation.current) {
        sending.current = false;
        setJoining(false);
      }
    }
  }
  return (
    <main className="co-signin co-company-choice">
      <h1>Company invitation</h1>
      {error ? (
        <>
          <p role="alert">{error}</p>
          <a href="/">Open my companies</a>
          <button onClick={() => location.reload()}>Check invitation again</button>
        </>
      ) : !invitation ? (
        <p role="status">Checking invitation…</p>
      ) : (
        <>
          <p>
            You’ve been invited to <strong>{invitation.companyName}</strong> as{" "}
            {invitation.role === "approver" ? "an" : "a"} {invitation.role}.
          </p>
          <p>
            Joining shares your name and email with this company’s members. You’ll have access to its shared workspace
            and use its existing allowance.
          </p>
          {!invitation.signedIn ? (
            <a className="co-primary" href="/signin?join=1">
              Sign in to review invitation
            </a>
          ) : !invitation.matches ? (
            <>
              <p role="alert">
                This invite is for a different verified account. You’re signed in as{" "}
                {invitation.accountEmail || "an account without a verified email"}.
              </p>
              <a href="/signin?join=1&account=1">Use another account</a>
            </>
          ) : (
            <>
              <p>Joining as {invitation.accountEmail}.</p>
              <button className="co-primary" disabled={joining} onClick={() => void join()}>
                {joining ? "Joining…" : "Join " + invitation.companyName}
              </button>
            </>
          )}
          <p>
            <a href="/">Keep using HireZero without joining</a>
          </p>
        </>
      )}
    </main>
  );
}
