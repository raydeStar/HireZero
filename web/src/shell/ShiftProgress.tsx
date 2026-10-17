import { useEffect, useState } from "react";
import { api, type AgentState, when } from "./api";

export function ShiftProgress({ agent }: { agent: AgentState }) {
  const progress = agent.progress;
  return (
    <section aria-label="Shift progress" className="fe-cockpit-section" role="status">
      <h3>{progress.state}</h3>
      <p>{progress.summary}</p>
      {progress.at && (
        <small>
          Last evidence: {when(progress.at)}
          {progress.freshness === "stale" ? " — overdue; worker status is unknown" : ""}
        </small>
      )}
      {progress.nextCheckAt && <p className="fe-muted">Next bounded check: {when(progress.nextCheckAt)}.</p>}
      <p className="fe-muted">
        Execution queue: {agent.tasks.filter((item) => item.state === "queued").length} queued. Proposals and approvals
        remain in their conversations.
      </p>
      {agent.recent.length > 0 && (
        <p className="fe-muted">
          Latest task: {agent.recent[0].title} —{" "}
          {agent.recent[0].state === "done" ? "Completed" : agent.recent[0].state}.
        </p>
      )}
    </section>
  );
}
export function HomeShiftProgress() {
  const [agent, setAgent] = useState<AgentState | null>(null),
    [fresh, setFresh] = useState(false);
  useEffect(() => {
    let live = true;
    async function load() {
      try {
        const value = await api<AgentState>("/agent");
        if (live) {
          setAgent(value);
          setFresh(true);
        }
      } catch {
        if (live) setFresh(false);
      }
    }
    void load();
    const timer = setInterval(() => {
      if (document.visibilityState === "visible") void load();
    }, 5000);
    return () => {
      live = false;
      clearInterval(timer);
    };
  }, []);
  return (
    <>
      {agent && <ShiftProgress agent={agent} />}{" "}
      {!fresh && (
        <p className="fe-muted" role="status">
          Shift status has not refreshed; execution freshness is unknown.
        </p>
      )}
    </>
  );
}
