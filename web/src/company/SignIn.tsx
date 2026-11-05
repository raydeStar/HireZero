import { useEffect, useRef, useState, type ReactNode } from "react";
import { api, ApiError, bindMemberSession, company, savePreference } from "../lib/api";

type CompanyChoice = { id: string; name: string; role: string };
export type Session = {
  login?: string;
  browserScope?: string;
  role?: string;
  signedIn?: boolean;
  accessMode?: string;
  companySelectionRequired?: boolean;
  companies?: CompanyChoice[];
  openSignup?: boolean;
};
const sessionSignal = "hirezero-session-change";

async function readSession() {
  const session = await api<Session>("/session", "GET", undefined, { timeoutMs: 10000 });
  if (
    !session ||
    !["local", "hosted"].includes(session.accessMode || "") ||
    typeof session.signedIn !== "boolean" ||
    (session.companySelectionRequired !== undefined && typeof session.companySelectionRequired !== "boolean") ||
    (session.companySelectionRequired &&
      (!session.signedIn ||
        !Array.isArray(session.companies) ||
        session.companies.some(
          (choice) =>
            !choice ||
            typeof choice.id !== "string" ||
            !/^[a-z0-9][a-z0-9-]{0,79}$/.test(choice.id) ||
            typeof choice.name !== "string" ||
            !choice.name.trim() ||
            !["owner", "approver", "viewer"].includes(choice.role),
        ) ||
        new Set(session.companies.map((choice) => choice.id)).size !== session.companies.length)) ||
    (session.signedIn &&
      (!session.login ||
        !/^[a-f0-9]{64}$/.test(session.browserScope || "") ||
        (!session.companySelectionRequired && !["owner", "approver", "viewer"].includes(session.role || ""))))
  )
    throw new Error("The session response could not be verified. Check sign-in again.");
  return session;
}

// Hosted mode (docs/adr/0020): members sign in through the company's identity provider before anything loads. In
// local mode the one owner is always signed in.
export function SignInGate({ children }: { children: (session: Session, signOut?: () => void) => ReactNode }) {
  const [session, setSession] = useState<Session | null>(null),
    [error, setError] = useState(""),
    [checking, setChecking] = useState(true),
    [signingOut, setSigningOut] = useState(false),
    [signOutError, setSignOutError] = useState(""),
    [creating, setCreating] = useState(false),
    [signupError, setSignupError] = useState("");
  const current = useRef<Session | null>(null),
    request = useRef(0),
    loading = useRef(false),
    leaving = useRef(false);
  useEffect(() => {
    let mounted = true;
    async function load() {
      if (loading.current || leaving.current) return;
      loading.current = true;
      const generation = ++request.current;
      setChecking(true);
      try {
        const next = await readSession();
        if (!mounted || generation !== request.current) return;
        if (
          current.current &&
          next.signedIn &&
          (current.current.browserScope !== next.browserScope || current.current.role !== next.role)
        ) {
          // Recreate every in-memory draft/receipt cache before another account or role can use the page.
          window.location.reload();
          return;
        }
        if (next.companySelectionRequired || (next.accessMode === "hosted" && !next.signedIn)) bindMemberSession();
        else bindMemberSession(next.accessMode, next.browserScope);
        current.current = next;
        setSession(next);
        setError("");
        setChecking(false);
      } catch (cause) {
        if (!mounted || generation !== request.current) return;
        const e = cause as Error;
        if (e instanceof ApiError && (e.status === 403 || e.status === 404) && company()) {
          savePreference("company", "");
          window.location.replace("/");
          return;
        }
        bindMemberSession();
        setError(e.message);
        setChecking(false);
      } finally {
        if (generation === request.current) loading.current = false;
      }
    }
    const recheck = () => {
      if (current.current?.accessMode === "hosted") void load();
    };
    const visible = () => {
      if (document.visibilityState === "visible") recheck();
    };
    const storage = (event: StorageEvent) => {
      if (event.key === sessionSignal) recheck();
    };
    const invalidated = () => void load();
    void load();
    window.addEventListener("focus", recheck);
    window.addEventListener("pageshow", recheck);
    window.addEventListener("storage", storage);
    window.addEventListener("hirezero-session-invalidated", invalidated);
    document.addEventListener("visibilitychange", visible);
    return () => {
      mounted = false;
      request.current++;
      window.removeEventListener("focus", recheck);
      window.removeEventListener("pageshow", recheck);
      window.removeEventListener("storage", storage);
      window.removeEventListener("hirezero-session-invalidated", invalidated);
      document.removeEventListener("visibilitychange", visible);
    };
  }, []);
  async function signup() {
    if (creating || !session?.browserScope) return;
    setCreating(true);
    bindMemberSession(session.accessMode, session.browserScope);
    try {
      const created = await api<CompanyChoice>("/signup", "POST", {});
      savePreference("company", created.id);
      window.location.assign("/?company=" + encodeURIComponent(created.id));
    } catch (cause) {
      setSignupError(
        (cause as Error).message + " Refresh access to check whether your company was created before trying again.",
      );
    }
  }
  async function signOut() {
    if (leaving.current) return;
    leaving.current = true;
    request.current++;
    bindMemberSession();
    setSigningOut(true);
    setSignOutError("");
    try {
      // Expiring the cookie is idempotent, but a lost response still needs an independent session read.
      await fetch("/signout", {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-Dev-Request": "1" },
        body: "{}",
        signal: AbortSignal.timeout(10000),
      }).catch(() => undefined);
      const next = await readSession();
      if (next.signedIn !== false || next.accessMode !== "hosted")
        throw new Error("Sign-out was not confirmed. Retry to end this session.");
      try {
        localStorage.setItem(sessionSignal, crypto.randomUUID());
      } catch {
        /* Other tabs also recheck on focus. */
      }
      window.location.replace("/");
    } catch (cause) {
      setSignOutError((cause as Error).message);
      leaving.current = false;
    }
  }
  if (signingOut)
    return (
      <main className="co-signin">
        <h1>HireZero</h1>
        {signOutError ? (
          <>
            <p role="alert">{signOutError}</p>
            <button onClick={() => void signOut()}>Retry sign out</button>
          </>
        ) : (
          <p role="status">Signing out…</p>
        )}
      </main>
    );
  if (error)
    return (
      <main className="co-signin">
        <p role="alert">{error}</p>
        <button onClick={() => window.location.reload()}>Retry sign-in check</button>
      </main>
    );
  if (!session)
    return (
      <main className="co-signin">
        <p role="status">Checking your session…</p>
      </main>
    );
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
  if (session.companySelectionRequired)
    return (
      <main className="co-signin co-company-choice" aria-busy={checking}>
        <h1>
          {session.companies!.length
            ? "Choose your company"
            : session.openSignup
              ? "Welcome to HireZero"
              : "No company access"}
        </h1>
        {checking ? (
          <p role="status">Checking your session…</p>
        ) : (
          <>
            <p>
              {session.companies!.length
                ? "You’re signed in. This company isn’t available to your account. Choose a company you belong to."
                : "You’re signed in, but this account has no active company membership. Ask a company owner to invite you, or sign in with another account."}
            </p>
            {session.openSignup && (
              <>
                <button className="co-primary" disabled={creating} onClick={() => void signup()}>
                  {creating ? "Checking company creation�" : "Create my company"}
                </button>
                {signupError && <p role="alert">{signupError}</p>}
              </>
            )}
            {session.companies!.length > 0 && (
              <ul aria-label="Your companies">
                {session.companies!.map((choice) => (
                  <li key={choice.id}>
                    <button
                      onClick={() => {
                        savePreference("company", choice.id === "local-company" ? "" : choice.id);
                        // A fresh company gets a fresh route; yesterday’s task must not follow the guest upstairs.
                        window.location.assign("/?company=" + encodeURIComponent(choice.id));
                      }}
                    >
                      <strong>{choice.name}</strong>
                      <span>
                        {choice.role === "owner" ? "Owner" : choice.role === "approver" ? "Approver" : "Viewer"}
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
            <div className="co-choice-actions">
              <button onClick={() => window.location.reload()}>Refresh access</button>
              {session.accessMode === "hosted" && <button onClick={() => void signOut()}>Sign out</button>}
            </div>
          </>
        )}
      </main>
    );
  return (
    <>
      {checking && (
        <main className="co-signin">
          <p role="status">Checking your session…</p>
        </main>
      )}
      <div hidden={checking}>
        {children(session, session.accessMode === "hosted" ? () => void signOut() : undefined)}
      </div>
    </>
  );
}
