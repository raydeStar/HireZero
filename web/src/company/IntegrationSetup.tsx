import { useEffect, useState } from "react";
import { api } from "../lib/api";

type Field = {
  id: string;
  label: string;
  help: string;
  maxLength: number;
  list: boolean;
  maxItems: number;
  value: string | string[] | null;
};
type Secret = { name: string; label: string; help: string; multiline: boolean; origin: string; pastedIgnored: boolean };
type Setup = {
  id: string;
  name: string;
  active: boolean;
  reason: string;
  restartNeeded: boolean;
  restartNote: string | null;
  file: string;
  localSecrets: boolean;
  fields: Field[];
  secrets: Secret[];
};

// Where a secret comes from, in words; never its value.
const origins: Record<string, string> = {
  missing: "Not set",
  app: "Set in the app",
  environment: "From an environment variable",
  file: "From a secret file",
  credential: "From a credential directory",
};

// Integration setup in the app (docs/adr/0038-integration-setup-in-the-app.md): settings are written to the same file
// the integration reads; in local mode a secret can be pasted, is stored encrypted and is never shown again.
export function IntegrationSetup({ refresh }: { refresh: () => Promise<void> }) {
  const [setups, setSetups] = useState<Setup[] | null>(null);
  const [error, setError] = useState("");
  async function load() {
    try {
      setSetups(await api<Setup[]>("/company/integrations/setup"));
    } catch (e) {
      setError((e as Error).message);
    }
  }
  useEffect(() => {
    void load();
  }, []);
  if (!setups) return error ? <p role="alert">{error}</p> : null;
  return (
    <>
      {setups.map((setup) => (
        <SetupForm
          key={setup.id}
          setup={setup}
          saved={async () => {
            await load();
            await refresh();
          }}
        />
      ))}
    </>
  );
}

function SetupForm({ setup, saved }: { setup: Setup; saved: () => Promise<void> }) {
  const initial = () =>
    Object.fromEntries(setup.fields.map((f) => [f.id, Array.isArray(f.value) ? f.value.join("\n") : (f.value ?? "")]));
  const [values, setValues] = useState<Record<string, string>>(initial);
  const [secrets, setSecrets] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  async function act(work: () => Promise<unknown>, done: string) {
    setBusy(true);
    setError("");
    setMessage("");
    try {
      await work();
      setMessage(done);
      await saved();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <details className="co-setup" aria-label={setup.name + " setup"}>
      <summary>
        {setup.name} · {setup.active ? "active" : "not set up"}
      </summary>
      <p className="co-muted">{setup.reason}</p>
      {setup.restartNeeded && <p role="status">Set up. Restart HireZero to start using it.</p>}
      {setup.fields.length > 0 && (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void act(
              () =>
                api("/company/integrations/" + setup.id + "/settings", "PUT", {
                  values: Object.fromEntries(
                    setup.fields.map((f) => [
                      f.id,
                      f.list ? (values[f.id] ?? "").split(/\r?\n/).map((v) => v.trim()) : (values[f.id] ?? ""),
                    ]),
                  ),
                }),
              "Settings saved.",
            );
          }}
        >
          {setup.fields.map((f) => (
            <label key={f.id}>
              {f.label}
              <small className="co-muted">{f.help}</small>
              {f.list ? (
                <textarea
                  aria-label={f.label}
                  rows={Math.min(f.maxItems, 5)}
                  value={values[f.id] ?? ""}
                  onChange={(e) => setValues({ ...values, [f.id]: e.target.value })}
                />
              ) : (
                <input
                  aria-label={f.label}
                  maxLength={f.maxLength}
                  value={values[f.id] ?? ""}
                  onChange={(e) => setValues({ ...values, [f.id]: e.target.value })}
                />
              )}
            </label>
          ))}
          <button disabled={busy} type="submit">
            Save settings
          </button>
          <small className="co-muted">Saved to {setup.file} in the data root; other keys there are kept.</small>
        </form>
      )}
      {setup.secrets.map((s) => (
        <div key={s.name} className="co-setup-secret" role="group" aria-label={s.label}>
          <p>
            <strong>{s.label}</strong> · {origins[s.origin] ?? s.origin} <small className="co-muted">({s.name})</small>
          </p>
          <small className="co-muted">{s.help}</small>
          {s.pastedIgnored && (
            <p className="co-muted">A value pasted here is kept but not used: the delivered one takes precedence.</p>
          )}
          {setup.localSecrets ? (
            <form
              onSubmit={(e) => {
                e.preventDefault();
                void act(
                  () =>
                    api("/company/integrations/" + setup.id + "/secrets", "PUT", {
                      name: s.name,
                      value: secrets[s.name] ?? "",
                    }),
                  s.label + " saved.",
                ).then(() => setSecrets((all) => ({ ...all, [s.name]: "" })));
              }}
            >
              {s.multiline ? (
                <textarea
                  aria-label={"Paste " + s.label}
                  autoComplete="off"
                  spellCheck={false}
                  rows={4}
                  value={secrets[s.name] ?? ""}
                  onChange={(e) => setSecrets({ ...secrets, [s.name]: e.target.value })}
                />
              ) : (
                <input
                  aria-label={"Paste " + s.label}
                  type="password"
                  autoComplete="new-password"
                  value={secrets[s.name] ?? ""}
                  onChange={(e) => setSecrets({ ...secrets, [s.name]: e.target.value })}
                />
              )}
              <div className="co-actions">
                <button disabled={busy || !(secrets[s.name] ?? "").trim()} type="submit">
                  {s.origin === "app" ? "Replace" : "Save"}
                </button>
                {(s.origin === "app" || s.pastedIgnored) && (
                  <button
                    disabled={busy}
                    type="button"
                    onClick={() =>
                      void act(
                        () =>
                          api("/company/integrations/" + setup.id + "/secrets", "PUT", { name: s.name, remove: true }),
                        s.label + " removed.",
                      )
                    }
                  >
                    Remove
                  </button>
                )}
              </div>
              <small className="co-muted">
                Stored encrypted on this computer and never shown again. A vault or environment value takes precedence.
              </small>
            </form>
          ) : (
            <p className="co-muted">Deliver {s.name} through your vault or platform (see docs/SECRETS.md).</p>
          )}
        </div>
      ))}
      {setup.restartNote && <p className="co-muted">{setup.restartNote}</p>}
      {message && <p role="status">{message}</p>}
      {error && <p role="alert">{error}</p>}
    </details>
  );
}
