import { useEffect, useRef, useState } from "react";
import { api, ApiError } from "../lib/api";

export type DocumentSource = {
  provider: string;
  externalId: string;
  version: string;
  modifiedAt: string;
  copiedAt: string;
};
export type DocumentOrigin = { source: DocumentSource; receipt: string; textEdited: boolean };
export type DocumentImport = {
  title: string;
  text: string;
  sourceName: string;
  notice: string;
  sourceReceipt: string;
  source: DocumentSource;
};
type SourceCheck = { documentId: string; documentVersion: number; state: string; preview: DocumentImport };
type SourceDocument = { id: string; version: number; digest: string; state: string; origin?: DocumentOrigin | null };

export function DocumentSourceReview({
  document,
  owner,
  blocked,
  onRevision,
}: {
  document: SourceDocument;
  owner: boolean;
  blocked: boolean;
  onRevision: (preview: DocumentImport) => void;
}) {
  const [result, setResult] = useState<SourceCheck | null>(null),
    [error, setError] = useState(""),
    [checking, setChecking] = useState(false);
  const generation = useRef(0);
  useEffect(() => {
    generation.current++;
    setResult(null);
    setError("");
    setChecking(false);
    return () => {
      generation.current++;
    };
  }, [document.id, document.version, document.digest]);
  const origin = document.origin;
  if (!origin || origin.source.provider !== "google-drive") return null;
  const source = origin.source;
  async function check() {
    if (checking) return;
    const current = ++generation.current;
    setChecking(true);
    setResult(null);
    setError("");
    try {
      const value = await api<SourceCheck>(
        `/company/documents/${encodeURIComponent(document.id)}/check-source`,
        "POST",
        {
          expectedVersion: document.version,
          digest: document.digest,
        },
        { timeoutMs: 35000 },
      );
      if (current === generation.current) setResult(value);
    } catch (cause) {
      if (current === generation.current)
        setError(
          cause instanceof ApiError && cause.code === "outcome-unknown"
            ? "The source check did not finish. Your saved copy is unchanged; try again."
            : (cause as Error).message,
        );
    } finally {
      if (current === generation.current) setChecking(false);
    }
  }
  return (
    <aside className="co-document-origin" aria-label="Recorded document source">
      <h4>Recorded Drive source</h4>
      <p>
        Version {source.version} · modified {new Date(source.modifiedAt).toLocaleString()} · copied{" "}
        {new Date(source.copiedAt).toLocaleString()}
      </p>
      <p>
        {origin.textEdited
          ? "Text was edited after import. The source record describes the imported copy, not every statement in this document."
          : "Saved text matches the imported copy."}
      </p>
      <p>Source changes do not replace approved knowledge. A check reads only this allowed file.</p>
      <a
        href={`https://drive.google.com/file/d/${encodeURIComponent(source.externalId)}/view`}
        target="_blank"
        rel="noopener noreferrer"
      >
        Open original in Drive
      </a>
      {owner && ["approved", "revoked"].includes(document.state) && (
        <>
          <div className="co-document-source-actions">
            <button disabled={checking} onClick={() => void check()}>
              {checking ? "Checking Drive source…" : "Check Drive source"}
            </button>
          </div>
          {error && <p role="alert">{error} Your saved document is unchanged.</p>}
          {result && (
            <div role="status">
              <p>
                {result.state === "unchanged"
                  ? "Drive's extracted text and version match the recorded import. Your saved edits are unchanged."
                  : result.state === "metadata-changed"
                    ? "Drive metadata changed; its extracted text still matches the recorded import. Your saved copy is unchanged."
                    : "Drive text changed. Your saved copy is unchanged."}
              </p>
              <p>
                Checked {new Date(result.preview.source.copiedAt).toLocaleString()} · current Drive version{" "}
                {result.preview.source.version}
              </p>
              {result.state !== "unchanged" && (
                <>
                  <details>
                    <summary>Review the latest source text</summary>
                    <pre>{result.preview.text}</pre>
                  </details>
                  <p>
                    A revised draft starts from this source copy. Keep any owner corrections you still need before
                    saving. The current document stays approved until you revoke it.
                  </p>
                  {blocked && (
                    <p>
                      Finish or discard your current draft and resolve any pending receipt first. Its text is preserved.
                    </p>
                  )}
                  <button disabled={blocked} onClick={() => onRevision(result.preview)}>
                    Draft a revision from this copy
                  </button>
                </>
              )}
            </div>
          )}
        </>
      )}
    </aside>
  );
}
