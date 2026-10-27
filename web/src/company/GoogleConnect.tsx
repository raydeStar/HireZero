import { useEffect, useRef, useState } from "react";
import { api } from "../lib/api";
import type { Setup } from "./IntegrationSetup";

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
  const starting = useRef(false);
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
    let current = true;
    setWaiting(false);
    setError("");
    Promise.all([api<Choices>("/connections/google/choices"), api<Setup[]>("/company/integrations/setup")])
      .then(([found, setups]) => {
        if (!current) return;
        const setup = setups.find((item) => item.id === "google-analytics");
        if (!setup) throw new Error("Saved Google settings could not be loaded.");
        const saved = (id: string) => {
          const value = setup.fields.find((field) => field.id === id)?.value;
          return typeof value === "string" ? value.trim() : "";
        };
        // Keep the owner's selections; alphabetical order is not a change request.
        const savedProperty = saved("ga4Property").replace(/^properties\//, "");
        const savedSite = saved("searchConsoleSite");
        const selectedProperty = found.properties.some((item) => item.value === savedProperty) ? savedProperty : "";
        const selectedSite = found.sites.some((item) => item.value === savedSite) ? savedSite : "";
        const unavailable = (savedProperty && !selectedProperty) || (savedSite && !selectedSite);
        setChoices({
          ...found,
          problems: unavailable
            ? [
                ...found.problems,
                "A saved selection is unavailable to this account. Review the choices before saving; your settings have not changed.",
              ]
            : found.problems,
        });
        setProperty(selectedProperty);
        setSite(selectedSite);
      })
      .catch((e: Error) => current && setError(e.message));
    return () => {
      current = false;
    };
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
        {message && <p role="status">{message}</p>}
        <span className="co-muted">
          {waiting
            ? "Finish signing in in the new tab; this updates by itself."
            : "Read-only access to Analytics and Search Console."}
        </span>
        <button
          className="co-primary"
          disabled={busy}
          onClick={async () => {
            if (starting.current) return;
            starting.current = true;
            setBusy(true);
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
            } finally {
              starting.current = false;
              setBusy(false);
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
      <button
        type="button"
        disabled={busy}
        onClick={async () => {
          setBusy(true);
          setError("");
          try {
            const result = await api<{ analyticsActive: boolean }>("/connections/google", "DELETE");
            setChoices(null);
            setStatus((current) => current && { ...current, signedIn: false });
            const url = new URL(window.location.href);
            url.searchParams.delete("google");
            window.history.replaceState(window.history.state, "", url);
            setMessage(
              result.analyticsActive
                ? "Google sign-in removed. This company's separate service account still provides analytics access."
                : "Google sign-in removed for this company.",
            );
            await load();
            onReady?.();
          } catch (failure) {
            setError((failure as Error).message);
          } finally {
            setBusy(false);
          }
        }}
      >
        Sign out of Google
      </button>
      {error && <p role="alert">{error}</p>}
    </form>
  );
}
