export type ScheduledBrowser = {
  itinerary: {
    name: string;
    origins: string[];
    steps: { action: string; value: string }[];
    limits: { seconds: number; requests: number; bytes: number };
    width: number;
    height: number;
    screenshots: boolean;
  };
};

export function ScheduledBrowserReview({ browser, remaining }: { browser: ScheduledBrowser; remaining: number }) {
  const review = browser.itinerary;
  const allocation = review.limits.seconds + 12;
  const actions: Record<string, string> = {
    navigate: "Open page",
    click: "Click",
    "wait-for": "Wait for",
    read: "Save page text",
  };
  return (
    <section className="co-schedule-script co-schedule-browser" aria-label="Recurring website review">
      <h4>{review.name}</h4>
      <p>
        {remaining} remaining visits · {review.limits.seconds} seconds per visit. Each occurrence reserves {allocation}{" "}
        seconds including startup and cleanup, up to {allocation * remaining} seconds across the remaining visits.
      </p>
      <details open>
        <summary>Exact websites and steps</summary>
        <ul>
          {review.origins.map((origin) => (
            <li key={origin}>{origin}</li>
          ))}
        </ul>
        <ol>
          {review.steps.map((step, i) => (
            <li key={i}>
              <b>{actions[step.action] || step.action}</b>: {step.value}
            </li>
          ))}
        </ol>
        <p>
          Up to {review.limits.requests} web requests and{" "}
          {(review.limits.bytes / 1048576).toLocaleString(undefined, { maximumFractionDigits: 1 })} MiB downloaded per
          visit. {review.width} × {review.height} viewport. Screenshots {review.screenshots ? "included" : "disabled"}.
          No sign-in or private browser profile is used.
        </p>
      </details>
      <p>
        Each visit saves dated page evidence for your review. Analysis and changes to company facts need separate
        approval. Cleanup must finish before the next occurrence.
      </p>
      <p>
        This schedule has its own approval. Use Pause schedule or End schedule to stop future visits; discarding the
        original one-off proposal does not end it. Saved results remain available.
      </p>
    </section>
  );
}
