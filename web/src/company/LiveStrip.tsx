import { ArrowRight } from "lucide-react";
import type { Snapshot, Work } from "./api";
import { workGroups, workState, workTitle, type Need } from "./Cockpit";
import { elapsed, useNow } from "./time";
import { useMemberAccess } from "./MemberAccess";

// What is happening now, on every page (UX round 10): running work with how long it has run, or, when the cockpit is
// out of view, what needs you. Each opens the place to look; nothing here acts.
export function LiveStrip({
  snapshot,
  needs,
  showNeeds,
  onOpenWork,
  onOpenNeed,
}: {
  snapshot: Snapshot;
  needs: Need[];
  showNeeds: boolean;
  onOpenWork: (task: Work) => void;
  onOpenNeed: (need: Need) => void;
}) {
  const { canWork } = useMemberAccess();
  const now = useNow(5_000);
  const d = snapshot.data;
  const running = d.tasks.filter((t) => (workGroups.progress as readonly string[]).includes(workState(snapshot, t)));
  const first = running[0];
  if (!first && !(showNeeds && needs.length)) return null;
  return (
    <div className={"co-live-strip" + (first ? " running" : " attention")} role="status" aria-live="polite">
      <span className="co-live-dot" aria-hidden="true" />
      {first ? (
        <>
          <span className="co-live-text">
            <b>{d.employees.find((e) => e.id === first.employeeId)?.name ?? "An employee"}</b> is working on{" "}
            {workTitle(snapshot, first)} · {elapsed(d.taskTimes?.[first.id]?.started, now)}
            {running.length > 1 && " · " + (running.length - 1) + " more running"}
          </span>
          <button className="co-live-open" onClick={() => onOpenWork(first)}>
            Watch <ArrowRight size={14} aria-hidden="true" />
          </button>
        </>
      ) : (
        <>
          <span className="co-live-text">
            <b>
              {needs.length} {needs.length === 1 ? "item needs" : "items need"} {canWork ? "you" : "attention"}
            </b>{" "}
            · {needs[0]!.title}
          </span>
          <button className="co-live-open" onClick={() => onOpenNeed(needs[0]!)}>
            Open <ArrowRight size={14} aria-hidden="true" />
          </button>
        </>
      )}
    </div>
  );
}
