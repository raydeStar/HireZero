import { useEffect, useState } from "react";
import { api } from "../lib/api";

type Check = { intact: boolean; count: number; head: string; brokenAt?: number; reason?: string };

// The company's tamper-evident audit chain (docs/adr/0022): whether it verifies, and an NDJSON export whose last line
// is the verified head. Keep exported heads elsewhere: a later export must still reach them.
export function Audit({ role, version, open }: { role?: string; version: number; open?: boolean }) {
  const [check, setCheck] = useState<Check | null>(null),
    [error, setError] = useState("");
  useEffect(() => {
    api<Check>("/company/audit/verify")
      .then((result) => {
        setCheck(result);
        setError("");
      })
      .catch((e: Error) => setError(e.message));
  }, [version]);
  async function download() {
    setError("");
    try {
      const contents = await api<Blob>("/company/audit/export", "GET", undefined, { response: "blob" });
      const url = URL.createObjectURL(contents);
      const link = document.createElement("a");
      link.href = url;
      link.download = "hirezero-audit.ndjson";
      link.click();
      URL.revokeObjectURL(url);
    } catch (e) {
      setError((e as Error).message);
    }
  }
  return (
    <details className="co-audit" open={open}>
      <summary>Audit log: {check ? (check.intact ? `intact, ${check.count} entries` : "broken") : "checking"}</summary>
      {error && <p role="alert">{error}</p>}
      {check && !check.intact && (
        <p role="alert">
          The chain breaks at entry {check.brokenAt}: {check.reason}.
        </p>
      )}
      {check && <p className="co-muted">Head {check.head.slice(0, 16)}…</p>}
      {(role === "owner" || role === "approver") && (
        <button type="button" onClick={() => void download()}>
          Export (NDJSON)
        </button>
      )}
    </details>
  );
}
