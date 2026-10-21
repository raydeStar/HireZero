import { useEffect, useState } from "react";
import { api } from "../lib/api";

type Google = { configured: boolean; signedIn: boolean; redirectUri: string; local: boolean };
type Choice = { value: string; label: string };
type Choices = { properties: Choice[]; sites: Choice[]; problems: string[] };

// Google sign-in for Analytics and Search Console (docs/adr/0049-google-sign-in.md): once the owner's OAuth client is
// saved, "Sign in with Google" opens Google's consent page in a new tab; afterwards the owner picks a GA4 property and
// a Search Console site from what the account can read, instead of typing ids. Used in a Connect card and on the
// Integrations page.
export function GoogleConnect({ onReady }: { onReady?: () => void }) {
  const [status, setStatus] = useState<Google | null>(null);
  const [choices, setChoices] = useState<Choices | null>(null);
  const [property, setProperty] = useState("");
  const [site, setSite] = useState("");
  const [waiting, setWaiting] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const load = () =>
    api<Google>("/connections/google")
      .then(setStatus)
      .catch((e: Error) => setError(e.message));
  useEffect(() => {
    void load();
  }, []);
  // Until signed in, check every few seconds: the client may be saved in the form below, or the sign-in tab finish.
  useEffect(() => {
    if (status?.signedIn) return;
    const timer = window.setInterval(() => void load(), 3000);
    return () => window.clearInterval(timer);
  }, [status?.signedIn]);
  useEffect(() => {
    if (!status?.signedIn) return;
    setWaiting(false);
    api<Choices>("/connections/google/choices")
      .then((found) => {
        setChoices(found);
        setProperty(found.properties[0]?.value ?? "");
        setSite(found.sites[0]?.value ?? "");
      })
      .catch((e: Error) => setError(e.message));
  }, [status?.signedIn]);

  if (!status) return error ? <p role="alert">{error}</p> : null;
  if (!status.local)
    return <p className="co-muted">Google sign-in is available when HireZero runs on this computer.</p>;
  if (!status.configured)
    return (
      <p className="co-muted">
        First, enter your Google OAuth client ID and secret below (see How to connect). Use{" "}
        <code>{status.redirectUri}</code> if Google asks for a redirect URI.
      </p>
    );
  if (!status.signedIn)
    return (
      <div className="co-connect-row">
        <span className="co-muted">
          {waiting
            ? "Finish signing in in the new tab; this updates by itself."
            : "Read-only access to Analytics and Search Console."}
        </span>
        <button
          className="co-primary"
          disabled={busy}
          onClick={async () => {
            // Opened during the click so the browser allows it; pointed at Google once the URL is ready.
            const tab = window.open("", "_blank");
            setError("");
            try {
              const { url } = await api<{ url: string }>("/connections/google/start", "POST", {});
              if (tab) tab.location.href = url;
              else window.location.href = url;
              setWaiting(true);
            } catch (failure) {
              tab?.close();
              setError((failure as Error).message);
            }
          }}
        >
          {waiting ? "Open sign-in again" : "Sign in with Google"}
        </button>
        {error && <p role="alert">{error}</p>}
      </div>
    );
  return (
    <form
      className="co-connect-code"
      onSubmit={async (e) => {
        e.preventDefault();
        setBusy(true);
        setError("");
        setMessage("");
        try {
          await api("/company/integrations/google-analytics/settings", "PUT", {
            values: { ga4Property: property, searchConsoleSite: site },
          });
          setMessage("Saved.");
          onReady?.();
        } catch (failure) {
          setError((failure as Error).message);
        } finally {
          setBusy(false);
        }
      }}
    >
      <span>Signed in with Google. Pick what HireZero reads:</span>
      {choices ? (
        <>
          <label>
            GA4 property
            <select value={property} onChange={(e) => setProperty(e.target.value)}>
              <option value="">None</option>
              {choices.properties.map((p) => (
                <option key={p.value} value={p.value}>
                  {p.label} ({p.value})
                </option>
              ))}
            </select>
          </label>
          <label>
            Search Console site
            <select value={site} onChange={(e) => setSite(e.target.value)}>
              <option value="">None</option>
              {choices.sites.map((s) => (
                <option key={s.value} value={s.value}>
                  {s.label}
                </option>
              ))}
            </select>
          </label>
          {choices.problems.map((p) => (
            <small key={p} className="co-muted">
              {p}
            </small>
          ))}
          <button className="co-primary" disabled={busy || (!property && !site)} type="submit">
            Save
          </button>
        </>
      ) : (
        <span className="co-muted">Reading what this account can see…</span>
      )}
      {message && <p role="status">{message}</p>}
      {error && <p role="alert">{error}</p>}
    </form>
  );
}
