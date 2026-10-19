import { useEffect, useState, type FormEvent } from "react";
import { api, company, savePreference } from "../lib/api";

type Listing = { id: string; name: string; role: string };

// The companies you belong to in this data root (docs/adr/0021): switch between them, and as an owner create another
// (you become its owner) or rename the current one.
export function Companies({ role, version }: { role?: string; version: number }) {
  const [companies, setCompanies] = useState<Listing[]>([]),
    [name, setName] = useState(""),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  useEffect(() => {
    api<Listing[]>("/companies")
      .then((result) => {
        setCompanies(result);
        setError("");
      })
      .catch((e: Error) => setError(e.message));
  }, [version]);
  const current = companies.find((c) => c.id === company()) ?? companies.find((c) => c.id === "local-company");
  function open(id: string) {
    savePreference("company", id === "local-company" ? "" : id);
    window.location.assign("/");
  }
  async function act(work: () => Promise<unknown>) {
    setBusy(true);
    setError("");
    try {
      await work();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <details className="co-companies">
      <summary>Company: {current?.name ?? "Company"}</summary>
      {error && <p role="alert">{error}</p>}
      {companies.length > 1 && (
        <label>
          Switch to
          <select value={current?.id ?? ""} onChange={(e) => open(e.target.value)}>
            {companies.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name} ({c.role})
              </option>
            ))}
          </select>
        </label>
      )}
      {role === "owner" && (
        <form
          onSubmit={(e: FormEvent) => {
            e.preventDefault();
            void act(async () => {
              const created = await api<Listing>("/companies", "POST", { name });
              open(created.id);
            });
          }}
        >
          <label>
            New company
            <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Name" />
          </label>
          <button disabled={busy || !name.trim()} type="submit">
            Create
          </button>
          <button
            disabled={busy || !name.trim()}
            type="button"
            onClick={() => void act(() => api("/company/name", "POST", { name }).then(() => window.location.reload()))}
          >
            Rename this one
          </button>
        </form>
      )}
    </details>
  );
}
