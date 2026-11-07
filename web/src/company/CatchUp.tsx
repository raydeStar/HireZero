import { useEffect, useState } from "react";
import { company } from "../lib/api";
import type { Employee, Snapshot, Work } from "./api";
import { workTitle, type Need } from "./Cockpit";
import { EmployeeSays } from "./EmployeeSays";
import { readyDraft } from "./readyDrafts";

// When you were last here, per employee in this browser. It is read once on arrival and kept current while you stay.
export function useLastSeen(employeeId: string) {
  const key = `hirezero-seen-v1:${company() || "local-company"}:${employeeId}`;
  const [since, setSince] = useState<string | null>(null);
  const [arrived, setArrived] = useState(() => Date.now());
  useEffect(() => {
    setArrived(Date.now());
    try {
      setSince(localStorage.getItem(key));
    } catch {
      setSince(null);
    }
    const mark = () => {
      try {
        localStorage.setItem(key, new Date().toISOString());
      } catch {
        // Without storage the employee simply has nothing to catch you up on.
      }
    };
    mark();
    const timer = window.setInterval(mark, 30_000);
    window.addEventListener("pagehide", mark);
    return () => {
      mark();
      window.clearInterval(timer);
      window.removeEventListener("pagehide", mark);
    };
  }, [key]);
  return [since, arrived] as const;
}

type News = { finished: Work[]; stopped: Work[] };

// What happened to this employee's work since you were last here. Nothing on a first visit or a quick return.
export function catchUpNews(
  snapshot: Snapshot,
  employee: Employee,
  since: string | null,
  arrived: number,
): News | undefined {
  const from = since ? Date.parse(since) : NaN;
  if (Number.isNaN(from) || arrived - from < 10 * 60_000) return undefined;
  const times = snapshot.data.taskTimes ?? {};
  // Only what happened while you were away; work that finishes while you are here arrives in the chat itself.
  const after = (t: Work) => {
    const at = Date.parse(times[t.id]?.updated ?? "") || 0;
    return at > from && at <= arrived;
  };
  const mine = snapshot.data.tasks.filter(
    (t) => t.employeeId === employee.id && (t.kind ?? "work") === "work" && after(t),
  );
  const finished = mine.filter((t) => readyDraft(snapshot, t) || t.state === "completed");
  const stopped = mine.filter((t) => t.state === "failed");
  return finished.length || stopped.length ? { finished, stopped } : undefined;
}

const list = (names: string[]) =>
  names.length <= 1 ? (names[0] ?? "") : names.slice(0, -1).join(", ") + " and " + names.at(-1);

// The employee speaks first when there is news since you were last here (docs/adr/0185), and says nothing otherwise.
export function CatchUp({
  snapshot,
  employee,
  news,
  needs,
  onOpenWork,
  onOpenNeed,
}: {
  snapshot: Snapshot;
  employee: Employee;
  news: News;
  needs: Need[];
  onOpenWork: (task: Work) => void;
  onOpenNeed: (need: Need) => void;
}) {
  const name = (t: Work) => (t.scheduleRunId ? "a morning round" : `“${workTitle(snapshot, t)}”`);
  const finished = news.finished.slice(-3);
  const ready = news.finished.filter((t) => readyDraft(snapshot, t)).at(-1);
  const waiting = needs.filter((n) => n.employeeId === employee.id || !n.employeeId);
  return (
    <EmployeeSays name={employee.name} label={employee.name + " catching you up"}>
      <p>
        Welcome back.
        {finished.length > 0 &&
          ` While you were away I finished ${list(finished.map(name))}${
            news.finished.length > finished.length ? `, and ${news.finished.length - finished.length} more` : ""
          }.`}
        {news.stopped.length > 0 &&
          ` ${news.stopped.length === 1 ? name(news.stopped[0]!).replace(/^a /, "A ") : "Some of my work"} didn’t finish, so you may want to look at why.`}
        {waiting.length > 0 &&
          ` ${waiting.length === 1 ? "One thing needs" : `${waiting.length} things need`} your go-ahead: ${waiting[0]!.title}.`}
      </p>
      <div className="co-actions">
        {ready && <button onClick={() => onOpenWork(ready)}>Read it</button>}
        {!ready && news.stopped[0] && <button onClick={() => onOpenWork(news.stopped[0]!)}>See what happened</button>}
        {waiting[0] && (
          <button className="co-primary" onClick={() => onOpenNeed(waiting[0]!)}>
            Show me
          </button>
        )}
      </div>
    </EmployeeSays>
  );
}
