import { useState } from "react";
import { dollars } from "./money";
import { useMemberAccess } from "./MemberAccess";

type Budget = { id: string; limit: number; used: number; reserved: number; version?: number };
export type Spend = {
  usd: number;
  pricedJobs: number;
  unpricedJobs: number;
  employees: { employeeId: string; usd: number; pricedJobs: number; unpricedJobs: number }[];
};

// Token budgets (ported from the legacy budget panel, Phase 2B.5j): each limit with what is used and reserved. You can
// set the company, department and employee limits within the server's ceilings; the allowance opened for one piece of
// work, plan or meeting is shown by name, and those reserving nothing fold away. Spend is in US dollars, from each
// authorized job's recorded usage and the reviewed price table (owner walkthrough, 2026-10-19).
export function Budgets({
  budgets,
  labels,
  departments,
  employees,
  spend,
  busy,
  run,
  open,
}: {
  open?: boolean;
  budgets: Budget[];
  labels?: Record<string, string>;
  departments: { id: string; name: string }[];
  employees: { id: string; name: string }[];
  spend?: Spend;
  busy: boolean;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
}) {
  const { canManage } = useMemberAccess();
  const [editing, setEditing] = useState<string | null>(null),
    [value, setValue] = useState("");
  const limits: { id: string; name: string; ceiling: number }[] = [
    { id: "company-total", name: "Company", ceiling: 1_000_000 },
    ...departments.map((d) => ({ id: "department:" + d.id, name: d.name, ceiling: 500_000 })),
    ...employees.map((e) => ({ id: "employee:" + e.id, name: e.name, ceiling: 250_000 })),
  ];
  const allowances = budgets.filter(
    (b) => !b.id.startsWith("department:") && !b.id.startsWith("employee:") && b.id !== "company-total",
  );
  const active = allowances.filter((b) => b.reserved > 0);
  const finished = allowances.filter((b) => b.reserved === 0);
  const byId = new Map(budgets.map((b) => [b.id, b]));
  const nameOf = (id: string) =>
    id === "company-research" ? "Company research" : (employees.find((e) => e.id === id)?.name ?? "Former employee");
  const tokens = (n: number) => n.toLocaleString();
  return (
    <details className="co-budgets" open={open}>
      <summary>Budgets</summary>
      <h3>Provider costs and estimates</h3>
      <p className="co-muted">
        Earlier jobs may use estimates at current prices. New jobs retain the prices captured when they start. Usage
        shows their monthly receipts and separates provider-reported costs from estimates.
      </p>
      {spend && spend.pricedJobs + spend.unpricedJobs > 0 ? (
        <>
          <table aria-label="Spend">
            <thead>
              <tr>
                <th>For</th>
                <th>Reported or estimated</th>
                <th>Jobs</th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td>Company</td>
                <td>{dollars(spend.usd)}</td>
                <td>{spend.pricedJobs + spend.unpricedJobs}</td>
              </tr>
              {spend.employees.map((line) => (
                <tr key={line.employeeId}>
                  <td>{nameOf(line.employeeId)}</td>
                  <td>{dollars(line.usd)}</td>
                  <td>{line.pricedJobs + line.unpricedJobs}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {spend.unpricedJobs > 0 && (
            <p className="co-muted">
              {spend.unpricedJobs} {spend.unpricedJobs === 1 ? "job" : "jobs"} ran on a model with no price (such as
              Codex, billed by its own plan), so {spend.unpricedJobs === 1 ? "it is" : "they are"} counted but not in
              dollars.
            </p>
          )}
        </>
      ) : (
        <p className="co-muted">No model jobs have run yet.</p>
      )}
      <h3>Cumulative safety limits</h3>
      {!canManage && (
        <p className="co-muted">An owner changes safety limits. Current usage and allowances are shown below.</p>
      )}
      <table aria-label="Limits">
        <thead>
          <tr>
            <th>For</th>
            <th>Used</th>
            <th>Reserved</th>
            <th>Limit</th>
          </tr>
        </thead>
        <tbody>
          {limits.map((row) => {
            const b = byId.get(row.id);
            return (
              <tr key={row.id}>
                <td>{row.name}</td>
                <td>{tokens(b?.used ?? 0)}</td>
                <td>{tokens(b?.reserved ?? 0)}</td>
                <td>
                  {canManage && editing === row.id ? (
                    <form
                      onSubmit={(e) => {
                        e.preventDefault();
                        void run("budget.configure", {
                          id: row.id,
                          expectedVersion: b?.version ?? 0,
                          budget: Number(value),
                        }).then((result) => {
                          if (result) setEditing(null);
                        });
                      }}
                    >
                      <input
                        aria-label={"Limit for " + row.name}
                        type="number"
                        required
                        disabled={busy}
                        min={0}
                        max={row.ceiling}
                        value={value}
                        onChange={(e) => setValue(e.target.value)}
                      />
                      <button disabled={busy} type="submit">
                        Save
                      </button>
                      <button disabled={busy} type="button" onClick={() => setEditing(null)}>
                        Cancel
                      </button>
                    </form>
                  ) : (
                    <>
                      {b ? tokens(b.limit) : "not set"}{" "}
                      {canManage && (
                        <button
                          disabled={busy}
                          type="button"
                          onClick={() => {
                            setEditing(row.id);
                            setValue(String(b?.limit ?? 0));
                          }}
                        >
                          Change
                        </button>
                      )}
                    </>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
      <p className="co-muted">
        Limits are in planning tokens. Ceilings: company 1,000,000, department 500,000, employee 250,000. A limit cannot
        drop below what is already used or reserved.
      </p>
      <h3>Work allowances</h3>
      {active.length ? (
        <Allowances rows={active} labels={labels} label="Holding tokens" />
      ) : (
        <p className="co-muted">No work is holding tokens right now.</p>
      )}
      {finished.length > 0 && (
        <details className="co-budgets-finished">
          <summary>Nothing reserved ({finished.length})</summary>
          <Allowances rows={finished} labels={labels} label="Nothing reserved" />
        </details>
      )}
    </details>
  );
}

// The allowance each plan, meeting or piece of work was given, by name.
function Allowances({ rows, labels, label }: { rows: Budget[]; labels?: Record<string, string>; label: string }) {
  return (
    <table aria-label={label}>
      <thead>
        <tr>
          <th>For</th>
          <th>Used</th>
          <th>Reserved</th>
          <th>Allowance</th>
        </tr>
      </thead>
      <tbody>
        {rows.map((b) => (
          <tr key={b.id}>
            <td>{labels?.[b.id] ?? "Earlier work"}</td>
            <td>{b.used.toLocaleString()}</td>
            <td>{b.reserved.toLocaleString()}</td>
            <td>{b.limit.toLocaleString()}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
