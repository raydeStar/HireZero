import { useState } from "react";

type Budget = { id: string; limit: number; used: number; reserved: number; version?: number };

// Token budgets (ported from the legacy budget panel, Phase 2B.5j): each limit with what is used and reserved. You can
// set the company, department and employee limits within the server's ceilings; plan allowances are approved with
// their plan and are shown here only.
export function Budgets({
  budgets,
  departments,
  employees,
  busy,
  run,
  open,
}: {
  open?: boolean;
  budgets: Budget[];
  departments: { id: string; name: string }[];
  employees: { id: string; name: string }[];
  busy: boolean;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
}) {
  const [editing, setEditing] = useState<string | null>(null),
    [value, setValue] = useState("");
  const rows: { id: string; name: string; ceiling?: number }[] = [
    { id: "company-total", name: "Company", ceiling: 1_000_000 },
    ...departments.map((d) => ({ id: "department:" + d.id, name: d.name, ceiling: 500_000 })),
    ...employees.map((e) => ({ id: "employee:" + e.id, name: e.name, ceiling: 250_000 })),
    ...budgets
      .filter((b) => !b.id.startsWith("department:") && !b.id.startsWith("employee:") && b.id !== "company-total")
      .map((b) => ({ id: b.id, name: "Plan allowance" })),
  ];
  const byId = new Map(budgets.map((b) => [b.id, b]));
  return (
    <details className="co-budgets" open={open}>
      <summary>Budgets</summary>
      <table>
        <thead>
          <tr>
            <th>For</th>
            <th>Used</th>
            <th>Reserved</th>
            <th>Limit</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => {
            const b = byId.get(row.id);
            return (
              <tr key={row.id}>
                <td>{row.name}</td>
                <td>{(b?.used ?? 0).toLocaleString()}</td>
                <td>{(b?.reserved ?? 0).toLocaleString()}</td>
                <td>
                  {editing === row.id && row.ceiling ? (
                    <form
                      onSubmit={(e) => {
                        e.preventDefault();
                        void run("budget.configure", {
                          id: row.id,
                          expectedVersion: b?.version ?? 0,
                          budget: Number(value),
                        }).then(() => setEditing(null));
                      }}
                    >
                      <input
                        aria-label={"Limit for " + row.name}
                        type="number"
                        min={0}
                        max={row.ceiling}
                        value={value}
                        onChange={(e) => setValue(e.target.value)}
                      />
                      <button disabled={busy} type="submit">
                        Save
                      </button>
                    </form>
                  ) : (
                    <>
                      {b ? b.limit.toLocaleString() : "not set"}{" "}
                      {row.ceiling && (
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
        Ceilings: company 1,000,000, department 500,000, employee 250,000 tokens. A limit cannot drop below what is
        already used or reserved.
      </p>
    </details>
  );
}
