import { useEffect, useRef, useState } from "react";
import { api } from "../lib/api";
import "./model-connection.css";
import { providerName, type ModelOption } from "./ModelChoice";

type Connection = {
  version: number;
  mode: string;
  provider: string | null;
  route: string | null;
  analysisRoute?: string | null;
  keyStored: boolean;
  status: string;
  canCombine?: boolean;
  choices?: ModelOption[];
  models: { route: string; name: string; price: { inputPerMillion: number; outputPerMillion: number } }[];
};

export function ModelConnection({ role, refresh }: { role?: string; refresh: () => Promise<void> }) {
  const owner = !role || role === "owner";
  const [current, setCurrent] = useState<Connection | null>(null);
  const [provider, setProvider] = useState("openai"),
    [route, setRoute] = useState("");
  const [analysisRoute, setAnalysisRoute] = useState("");
  const [keepHost, setKeepHost] = useState(false);
  const [key, setKey] = useState(""),
    [consent, setConsent] = useState(false),
    [remove, setRemove] = useState(false);
  const [busy, setBusy] = useState(false),
    [uncertain, setUncertain] = useState(false);
  const [error, setError] = useState(""),
    [notice, setNotice] = useState("");
  const lock = useRef(false),
    generation = useRef(0);
  async function load(initial = false) {
    const request = ++generation.current;
    const result = await api<Connection>("/company/model-connection", "GET", undefined, { timeoutMs: 15000 });
    if (request !== generation.current) return;
    setCurrent(result);
    if (initial) {
      setProvider(result.provider ?? "openai");
      setRoute(result.route ?? "");
      setAnalysisRoute(result.mode !== "host" ? (result.analysisRoute ?? "") : "");
      setKeepHost(!!result.canCombine && result.mode !== "company-key");
    }
    setUncertain(false);
    setConsent(false);
    setRemove(false);
  }
  useEffect(() => {
    if (owner) void load(true).catch((e) => setError((e as Error).message));
    return () => {
      generation.current++;
    };
  }, [owner]);
  const models = (current?.models ?? []).filter(
    (model) =>
      (provider === "openrouter" || model.route.startsWith(provider + "/")) &&
      (!keepHost ||
        !current?.choices?.some(
          (choice) => choice.route === model.route && (choice.provider === "codex-oauth" || choice.provider === "host"),
        )),
  );
  const selected = models.find((model) => model.route === route);
  const selectedAnalysis = models.find((model) => model.route === analysisRoute);
  async function change(removing: boolean) {
    if (
      lock.current ||
      !current ||
      uncertain ||
      (removing ? !remove : !consent || !selected || !key.trim() || (!!analysisRoute && !selectedAnalysis))
    )
      return;
    lock.current = true;
    setBusy(true);
    setError("");
    setNotice("");
    try {
      const next = await api<Connection>(
        "/company/model-connection",
        removing ? "DELETE" : "PUT",
        removing
          ? { expectedVersion: current.version, decision: "remove-key-and-use-host" }
          : {
              expectedVersion: current.version,
              provider,
              route,
              analysisRoute: analysisRoute || null,
              key,
              decision: keepHost ? "use-host-and-company-key" : "use-company-key",
            },
        { timeoutMs: 15000 },
      );
      setCurrent(next);
      setNotice(
        removing
          ? "Company key removed. New reviews use the host connection."
          : keepHost
            ? "API model added as a separate choice. The host remains the default. No provider request was made."
            : "Company key saved. No provider request was made. Prepare a new job to review its model and spend cap.",
      );
      await refresh().catch(() =>
        setError("The connection was saved, but the workspace could not refresh. Reload before preparing work."),
      );
    } catch (e) {
      // Re-read local state after an uncertain response. Even the butler does not submit a secret twice by guesswork.
      setUncertain(true);
      setError((e as Error).message + " Check saved connection before making another change.");
    } finally {
      setKey("");
      setConsent(false);
      setRemove(false);
      lock.current = false;
      setBusy(false);
    }
  }
  async function check() {
    if (lock.current) return;
    lock.current = true;
    setBusy(true);
    setError("");
    setNotice("");
    try {
      await load();
      setNotice(
        "Current saved connection loaded. Review it before making another change; keys are never returned to this page.",
      );
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      lock.current = false;
      setBusy(false);
    }
  }
  return (
    <section className="co-model-connection" aria-label="Company model connection">
      <h3>AI model connection</h3>
      {!owner ? (
        <p>A company owner can manage the model connection.</p>
      ) : (
        <>
          <p>
            Choose the account used for reviewed text jobs. API providers charge their accounts separately from a Codex
            subscription. HireZero subscription and monthly safety limits still apply.
          </p>
          {error && (
            <p className="co-error" role="alert">
              {error}
            </p>
          )}
          {notice && <p role="status">{notice}</p>}
          {current ? (
            <div className="co-model-current">
              <strong>
                {current.mode === "company-key"
                  ? "Company API key"
                  : current.mode === "host-and-company-key"
                    ? "Host default + separate API model"
                    : "Host connection"}
              </strong>
              {current.route && (
                <p>
                  {current.mode === "host-and-company-key" ? "Available API choice" : "Routine work"}:{" "}
                  {current.provider} · {current.route}
                </p>
              )}
              {current.analysisRoute && (
                <p>
                  {current.mode === "host-and-company-key" ? "Second API choice" : "Deeper analysis"}:{" "}
                  {current.analysisRoute}
                </p>
              )}
              <p>{current.status}</p>
              {current.choices?.map((choice) => (
                <p key={choice.route}>
                  {providerName(choice.provider)} · {choice.route} ·{" "}
                  {choice.allowanceUnit === "usd-cents" ? "US-cent cap" : "intended credit allowance"}
                </p>
              ))}
            </div>
          ) : (
            <p>Connection status is not loaded yet.</p>
          )}
          <button type="button" disabled={busy} onClick={() => void check()}>
            Check saved connection
          </button>
          {current && (
            <details open={!current.keyStored}>
              <summary>{current.keyStored ? "Manage API connection" : "Add an API connection"}</summary>
              <p>
                Changing this connection expires unsent model reviews. Review the new model and allowance before running
                work. Requests already sent keep their usage and any unresolved holds.
              </p>
              {current.models.length === 0 ? (
                <p>
                  No API models have an operator-reviewed price yet. Ask the host operator to configure the model price
                  catalog before adding a company key. The current connection remains available.
                </p>
              ) : (
                <form
                  onSubmit={(e) => {
                    e.preventDefault();
                    void change(false);
                  }}
                >
                  <fieldset disabled={busy || uncertain}>
                    <legend>{current.keyStored ? "Replace company API key" : "Add company API key"}</legend>
                    {current.canCombine && (
                      <label className="co-model-consent">
                        <input
                          type="checkbox"
                          checked={keepHost}
                          onChange={(event) => {
                            setKeepHost(event.target.checked);
                            setConsent(false);
                          }}
                        />
                        <span>
                          Keep the host connection as the default. Add this API model as a separate choice for each job.
                        </span>
                      </label>
                    )}
                    <label>
                      API provider
                      <select
                        value={provider}
                        onChange={(e) => {
                          setProvider(e.target.value);
                          setRoute("");
                          setAnalysisRoute("");
                          setKey("");
                          setConsent(false);
                        }}
                      >
                        <option value="openai">OpenAI</option>
                        <option value="anthropic">Anthropic</option>
                        <option value="openrouter">OpenRouter</option>
                      </select>
                    </label>
                    <label>
                      Model
                      <select
                        required
                        value={route}
                        onChange={(e) => {
                          setRoute(e.target.value);
                          setConsent(false);
                        }}
                      >
                        <option value="">Choose a priced model</option>
                        {models.map((model) => (
                          <option key={model.route} value={model.route}>
                            {model.name}
                          </option>
                        ))}
                      </select>
                    </label>
                    {!models.length && <p>No reviewed models are configured for this provider.</p>}
                    <p>
                      Use an economical model for routine chat, extraction and drafts. Specific planning and review work
                      can use a separate model below.
                    </p>
                    {selected && (
                      <p>
                        Model: {selected.route}. Configured budgeting rates per million tokens: $
                        {selected.price.inputPerMillion} input · ${selected.price.outputPerMillion} output. These may be
                        operator-defined ceilings rather than published prices. Each job must fit its reviewed spend
                        cap.
                      </p>
                    )}
                    <details className="co-model-analysis">
                      <summary>
                        {keepHost ? "Optional second API choice" : "Optional model for deeper analysis"}
                      </summary>
                      <p>
                        {keepHost
                          ? "Adds another API model you can explicitly select for an employee task or reply. Host defaults stay unchanged."
                          : "Used for planning, technical reviews and memory consistency checks. Other work stays on the routine model."}{" "}
                        Each request shows its exact model and still needs your approval.
                      </p>
                      <label>
                        {keepHost ? "Second API model" : "Analysis model"}
                        <select
                          value={analysisRoute}
                          onChange={(e) => {
                            setAnalysisRoute(e.target.value);
                            setConsent(false);
                          }}
                        >
                          <option value="">Use the routine model</option>
                          {models.map((model) => (
                            <option key={model.route} value={model.route}>
                              {model.name}
                            </option>
                          ))}
                        </select>
                      </label>
                      {selectedAnalysis && (
                        <p>
                          Listed estimate per million tokens: ${selectedAnalysis.price.inputPerMillion} input · $
                          {selectedAnalysis.price.outputPerMillion} output. These prices do not establish which model
                          will produce better work.
                        </p>
                      )}
                      <p>
                        A failed request is never retried on a different model. Switching models requires a fresh
                        review.
                      </p>
                    </details>
                    <label>
                      API key
                      <input
                        type="password"
                        autoComplete="off"
                        spellCheck={false}
                        required
                        minLength={8}
                        maxLength={8192}
                        value={key}
                        onChange={(e) => {
                          setKey(e.target.value);
                          setConsent(false);
                        }}
                      />
                    </label>
                    <p>
                      The key is encrypted on the server and never returned to this page. Saving it does not test
                      provider access or authorize a request. Code execution uses its separately configured runtime.
                    </p>
                    <label className="co-model-consent">
                      <input type="checkbox" checked={consent} onChange={(e) => setConsent(e.target.checked)} />
                      <span>
                        I authorize this company to use this API account for separately approved text jobs and accept
                        direct provider charges.
                      </span>
                    </label>
                    <button
                      type="submit"
                      disabled={!consent || !selected || !key.trim() || (!!analysisRoute && !selectedAnalysis)}
                    >
                      Save company connection
                    </button>
                  </fieldset>
                </form>
              )}
              {current.mode !== "host" && (
                <fieldset disabled={busy || uncertain}>
                  <legend>Return to the host connection</legend>
                  <label className="co-model-consent">
                    <input type="checkbox" checked={remove} onChange={(e) => setRemove(e.target.checked)} />
                    <span>
                      Remove the saved company key and use the host’s configured model for new reviews. This does not
                      revoke the key at the provider.
                    </span>
                  </label>
                  <button type="button" disabled={!remove} onClick={() => void change(true)}>
                    Remove key and use host
                  </button>
                </fieldset>
              )}
            </details>
          )}
        </>
      )}
    </section>
  );
}
