import { useEffect, useRef, useState, type FormEvent } from "react";
import { api, ApiError, company, savePreference } from "../lib/api";
import { RetirementReview } from "./RetirementReview";
import { WorkControl, type CompanyWorkControl } from "./WorkControl";
import {
  checkCompanyCreation,
  createCompany,
  finishCompanyCreation,
  pendingCompanyCreation,
  UnconfirmedCompanyCreation,
  type PendingCompanyCreation,
} from "./companyCreation";

type Listing = { id: string; name: string; role: string };

// The companies you belong to in this data root (docs/adr/0021): switch between them, and as an owner create another
// (you become its owner) or rename the current one.
export function Companies({
  role,
  version,
  expanded,
  workControl,
  onWorkControl,
}: {
  role?: string;
  version: number;
  expanded?: boolean;
  workControl?: CompanyWorkControl;
  onWorkControl?: (value: CompanyWorkControl) => void;
}) {
  const [companies, setCompanies] = useState<Listing[]>([]),
    [newName, setNewName] = useState(""),
    [name, setName] = useState(""),
    [dirty, setDirty] = useState(false),
    [message, setMessage] = useState(""),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false),
    [pendingCreation, setPendingCreation] = useState<PendingCompanyCreation | null>(null),
    [canFinishCreation, setCanFinishCreation] = useState(false),
    [creationStorageAvailable, setCreationStorageAvailable] = useState(true);
  const inFlight = useRef(false);
  function readCreationStatus() {
    try {
      setPendingCreation(pendingCompanyCreation());
      setCreationStorageAvailable(true);
    } catch {
      setCreationStorageAvailable(false);
    }
  }
  useEffect(readCreationStatus, []);
  async function creation(finish = false) {
    setCanFinishCreation(false);
    try {
      const created = finish
        ? await finishCompanyCreation()
        : pendingCreation
          ? await checkCompanyCreation()
          : await createCompany(newName.trim());
      open(created.id);
    } catch (cause) {
      setCanFinishCreation(
        cause instanceof UnconfirmedCompanyCreation
          ? cause.canFinish
          : cause instanceof ApiError && cause.code === "company-creation-not-recorded",
      );
      throw cause;
    }
  }
  useEffect(() => {
    let current = true;
    api<Listing[]>("/companies")
      .then((result) => {
        if (!current) return;
        setCompanies(result);
        setError("");
      })
      .catch((e: Error) => current && setError(e.message));
    return () => {
      current = false;
    };
  }, [version]);
  const current = companies.find((c) => c.id === company()) ?? companies.find((c) => c.id === "local-company");
  useEffect(() => {
    if (!dirty) setName(current?.name ?? "");
  }, [current?.id, current?.name, dirty]);
  function open(id: string) {
    savePreference("company", id === "local-company" ? "" : id);
    window.location.assign("/");
  }
  async function act(work: () => Promise<unknown>) {
    if (inFlight.current) return;
    inFlight.current = true;
    setBusy(true);
    setError("");
    setMessage("");
    try {
      await work();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      readCreationStatus();
      inFlight.current = false;
      setBusy(false);
    }
  }
  return (
    <details className="co-companies" open={expanded}>
      <summary>Company: {current?.name ?? "Company"}</summary>
      {error && <p role="alert">{error}</p>}
      {message && <p role="status">{message}</p>}
      {companies.length > 1 && (
        <label>
          Switch to
          <select disabled={busy} value={current?.id ?? ""} onChange={(e) => open(e.target.value)}>
            {companies.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name} ({c.role})
              </option>
            ))}
          </select>
        </label>
      )}
      {role === "owner" && (
        <>
          <form
            aria-label="Rename company"
            onSubmit={(e: FormEvent) => {
              e.preventDefault();
              const submitted = name.trim();
              void act(async () => {
                await api("/company/name", "POST", { name: submitted });
                setCompanies((all) =>
                  all.map((item) => (item.id === current?.id ? { ...item, name: submitted } : item)),
                );
                setDirty(false);
                setMessage("Company renamed.");
              });
            }}
          >
            <label>
              Company name
              <input
                required
                maxLength={80}
                disabled={busy || !current}
                value={name}
                onChange={(e) => {
                  setName(e.target.value);
                  setDirty(true);
                }}
              />
            </label>
            <button disabled={busy || !current || !name.trim() || name.trim() === current.name} type="submit">
              Rename company
            </button>
          </form>
          <form
            aria-label="Create company"
            onSubmit={(e: FormEvent) => {
              e.preventDefault();
              void act(() => creation());
            }}
          >
            {!pendingCreation && (
              <label>
                New company name
                <input
                  required
                  maxLength={80}
                  disabled={busy || !creationStorageAvailable}
                  value={newName}
                  onChange={(e) => setNewName(e.target.value)}
                />
              </label>
            )}
            {pendingCreation && (
              <p role="status">
                A company creation is waiting for confirmation. Check its saved result to open it without creating a
                duplicate.
                {pendingCreation.name && (
                  <>
                    {" "}
                    Name: <strong>{pendingCreation.name}</strong>.
                  </>
                )}
              </p>
            )}
            {!creationStorageAvailable && (
              <p role="alert">
                Allow session storage for this tab, then reload before creating a company. No creation request will be
                submitted.
              </p>
            )}
            <button disabled={busy || !creationStorageAvailable || (!pendingCreation && !newName.trim())} type="submit">
              {pendingCreation ? "Check saved company" : "Create company"}
            </button>
            {pendingCreation?.name && canFinishCreation && (
              <>
                <p>
                  No saved company was found yet. Finish this same creation using its original name; it cannot create a
                  second copy.
                </p>
                <button
                  type="button"
                  disabled={busy || !creationStorageAvailable}
                  onClick={() => void act(() => creation(true))}
                >
                  Finish this creation
                </button>
              </>
            )}
          </form>
          {expanded && workControl && onWorkControl && <WorkControl control={workControl} onSaved={onWorkControl} />}
          {expanded && <RetirementReview />}
        </>
      )}
    </details>
  );
}
