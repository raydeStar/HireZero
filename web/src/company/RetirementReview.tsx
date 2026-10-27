import { useEffect, useRef, useState } from "react";
import { api } from "../lib/api";
import "./retirement.css";

type Review = {
  checkedAt: string;
  collections: { collection: string; records: number }[];
  issues: { area: string; records: number; action: string }[];
  unverifiedAreas: string[];
};

export function RetirementReview() {
  const [review, setReview] = useState<Review>(),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const request = useRef(0),
    inFlight = useRef(false);
  useEffect(
    () => () => {
      request.current++;
    },
    [],
  );
  async function load() {
    if (inFlight.current) return;
    const generation = ++request.current;
    inFlight.current = true;
    setBusy(true);
    setReview(undefined);
    setError("");
    try {
      const result = await api<Review>("/company/retirement-review", "GET", undefined, { timeoutMs: 20000 });
      if (generation === request.current) setReview(result);
    } catch (cause) {
      if (generation === request.current) setError((cause as Error).message);
    } finally {
      if (generation === request.current) {
        inFlight.current = false;
        setBusy(false);
      }
    }
  }
  return (
    <section className="co-retirement" aria-label="Before closing this company">
      <h3>Before closing this company</h3>
      <p>Check for unfinished work, held usage and unresolved payments before planning a closure.</p>
      <p>
        <strong>Company deletion is not available yet.</strong> This review does not pause work, cancel subscriptions or
        erase data.
      </p>
      <button type="button" disabled={busy} onClick={() => void load()}>
        {busy ? "Checking company records…" : "Check company records"}
      </button>
      {busy && <p role="status">Checking the current company’s saved records…</p>}
      {error && <p role="alert">{error}</p>}
      {review && (
        <div aria-live="polite">
          <p>Checked {new Date(review.checkedAt).toLocaleString()}. Work can change after this check.</p>
          <p>
            {review.collections.reduce((sum, item) => sum + item.records, 0).toLocaleString()} saved database records
            inventoried. This is a record count, not a storage-size estimate.
          </p>
          {review.issues.length ? (
            <>
              <h4>Needs review</h4>
              <ul>
                {review.issues.map((issue) => (
                  <li key={issue.area}>
                    {issue.action}{" "}
                    <strong>
                      ({issue.records.toLocaleString()} {issue.records === 1 ? "record" : "records"})
                    </strong>
                  </li>
                ))}
              </ul>
            </>
          ) : (
            <p>
              No outstanding items were found in the checked records. This does not confirm that the company is ready
              for deletion.
            </p>
          )}
          <details>
            <summary>What still needs a separate check</summary>
            <ul>
              {review.unverifiedAreas.map((area) => (
                <li key={area}>{area}</li>
              ))}
            </ul>
          </details>
        </div>
      )}
    </section>
  );
}
