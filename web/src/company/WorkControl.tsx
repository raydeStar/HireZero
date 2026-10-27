import { useEffect, useRef, useState } from "react";
import { api } from "../lib/api";
import { command, reconcileCompanyOperation, UnconfirmedCompanyOperation } from "./api";

export type CompanyWorkControl = { paused: boolean; version: number; updatedAt: string };

export function WorkControl({
  control,
  onSaved,
}: {
  control: CompanyWorkControl;
  onSaved: (value: CompanyWorkControl) => void;
}) {
  const [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [message, setMessage] = useState(""),
    [consent, setConsent] = useState(false),
    [unconfirmed, setUnconfirmed] = useState<string>(),
    [current, setCurrent] = useState(true);
  const inFlight = useRef(false),
    generation = useRef(0);
  useEffect(() => {
    setConsent(false);
  }, [control.version]);
  useEffect(
    () => () => {
      generation.current++;
    },
    [],
  );
  async function act(readOnly: boolean) {
    if (inFlight.current || (!readOnly && (!current || unconfirmed || (control.paused && !consent)))) return;
    inFlight.current = true;
    const request = ++generation.current;
    setBusy(true);
    setError("");
    setMessage("");
    try {
      if (readOnly) {
        if (unconfirmed) {
          await reconcileCompanyOperation(unconfirmed);
          if (request !== generation.current) return;
          setUnconfirmed(undefined);
        }
        const next = await api<CompanyWorkControl>("/company/work-control", "GET", undefined, { timeoutMs: 15000 });
        if (request !== generation.current) return;
        onSaved(next);
        setCurrent(true);
        setMessage("Current work setting loaded.");
      } else {
        const next = (await command(
          "company.work.configure",
          {
            expectedVersion: control.version,
            decision: control.paused ? "resume-new-work" : "pause-new-work",
          },
          () => {},
        )) as CompanyWorkControl;
        if (request !== generation.current) return;
        onSaved(next);
        setConsent(false);
        setMessage(
          next.paused
            ? "New work is paused. Previously admitted work may still finish."
            : "New work may resume under its existing approvals and limits.",
        );
      }
    } catch (cause) {
      if (request !== generation.current) return;
      if (cause instanceof UnconfirmedCompanyOperation) setUnconfirmed(cause.requestId);
      setCurrent(false);
      setError((cause as Error).message);
    } finally {
      if (request === generation.current) {
        inFlight.current = false;
        setBusy(false);
      }
    }
  }
  return (
    <section className="co-work-control" aria-label="Company work controls">
      <h3>New work</h3>
      <p>
        <strong>{control.paused ? "Paused for this company" : "Allowed under existing approvals"}</strong>
      </p>
      <p>
        Pause new AI jobs, scripts, browsers, employee actions, research, scheduled occurrences and publications in one
        place.
      </p>
      <p>
        Work already admitted may still start or finish. Results, cleanup, notifications and billing remain available.
        This does not cancel a subscription or erase data.
      </p>
      {control.paused && (
        <label className="co-check">
          <input
            type="checkbox"
            checked={consent}
            disabled={busy || !current || !!unconfirmed}
            onChange={(event) => setConsent(event.target.checked)}
          />
          Allow existing queued work and approved schedules to run again. Expired approvals and missed-slot rules still
          apply.
        </label>
      )}
      <button
        type="button"
        disabled={busy || !current || !!unconfirmed || (control.paused && !consent)}
        onClick={() => void act(false)}
      >
        {busy ? "Checking work setting…" : control.paused ? "Resume new work" : "Pause new work"}
      </button>{" "}
      <button type="button" disabled={busy} onClick={() => void act(true)}>
        Refresh work status
      </button>
      {message && <p role="status">{message}</p>}
      {error && <p role="alert">{error} Refresh work status before another change.</p>}
    </section>
  );
}
