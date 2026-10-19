import { useEffect, useState, type ReactNode } from "react";
import { api } from "../lib/api";

export type Session = { login?: string; role?: string; signedIn?: boolean; accessMode?: string };

// Hosted mode (docs/adr/0020): members sign in through the company's identity provider before anything loads. In
// local mode the one owner is always signed in.
export function SignInGate({ children }: { children: (session: Session) => ReactNode }) {
  const [session, setSession] = useState<Session | null>(null),
    [error, setError] = useState("");
  useEffect(() => {
    api<Session>("/session")
      .then(setSession)
      .catch((e: Error) => setError(e.message));
  }, []);
  if (error) return <p role="alert">{error}</p>;
  if (!session) return null;
  if (session.accessMode === "hosted" && !session.signedIn) {
    const outcome = new URLSearchParams(window.location.search).get("signin");
    return (
      <main className="co-signin">
        <h1>HireZero</h1>
        {outcome === "not-invited" && (
          <p role="alert">That account has not been invited to this company. Ask an owner to invite your email.</p>
        )}
        {outcome === "failed" && <p role="alert">Sign-in did not finish. Try again.</p>}
        <a className="co-primary" href="/signin">
          Sign in
        </a>
      </main>
    );
  }
  return <>{children(session)}</>;
}
