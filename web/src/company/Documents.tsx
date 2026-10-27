import { memberStorage as sessionStorage } from "../lib/api";
import { useEffect, useRef, useState } from "react";
import { api, ApiError, company } from "../lib/api";
import { command, reconcileCompanyOperation, UnconfirmedCompanyOperation, type Snapshot } from "./api";
import "./documents.css";
import { DocumentSourceReview, type DocumentOrigin, type DocumentImport } from "./DocumentSource";

type Document = {
  id: string;
  title: string;
  text?: string;
  departmentId: string | null;
  employeeId: string | null;
  sourceName: string | null;
  replacesId: string | null;
  digest: string;
  state: string;
  version: number;
  updatedAt: string;
  deletionScope?: string | null;
  bytes?: number;
  origin?: DocumentOrigin | null;
};
type Draft = {
  title: string;
  text: string;
  audience: string;
  sourceName?: string;
  sourceReceipt?: string;
  id?: string;
  version?: number;
  replacesId?: string;
};
type Pending = { id: string; fingerprint: string; saving: boolean; retention?: boolean };
type DriveOptions = { ready: boolean; reason: string; fileIds: string[]; notice: string };
type Retention = { id: string; discardedDays: number; version: number };
type Retrieval = {
  notice: string;
  excerpts: { documentId: string; title: string; version: number; text: string; chunk: number; approvedAt: string }[];
};
const empty = (): Draft => ({ title: "", text: "", audience: "company" });
const deletionNotice = (doc: Document) =>
  doc.deletionScope === "revoked-source"
    ? "Library copy deleted. Earlier task excerpts, other drafts, downloads and backups have not been erased."
    : "Unapproved document deleted. Its saved text and document recovery copies were removed.";
function WordImportNotice() {
  return (
    <div className="co-word-import-notice">
      <p>Body text and tables only. Review the extracted text carefully. The original Word file is not retained.</p>
      <details>
        <summary>What this import leaves out</summary>
        <p>
          Headers, footers, comments, notes, images, text boxes, formatting and automatic list labels are omitted. Field
          values are not refreshed. Correct any missing context in the text before approving it.
        </p>
      </details>
    </div>
  );
}
function PdfImportNotice() {
  return (
    <div className="co-word-import-notice co-pdf-import-notice">
      <p>
        Selectable PDF text with page labels. Compare it with the original before approval. The original PDF is not
        retained.
      </p>
      <details>
        <summary>What this PDF import leaves out</summary>
        <p>
          Reading order, columns and tables may differ. Images, scanned text, annotations, attachments and form fields
          are omitted. Pages without selectable text are marked in the draft. Review every page and correct missing
          context; OCR is not included.
        </p>
      </details>
    </div>
  );
}
const prefix = "hirezero-documents-v1:" + (company() || "local-company");
function remembered<T>(key: string): T | null {
  try {
    return JSON.parse(sessionStorage.getItem(prefix + key) || "null") as T | null;
  } catch {
    return null;
  }
}

export function Documents({ snapshot, role }: { snapshot: Snapshot; role?: string }) {
  const [documents, setDocuments] = useState<Document[]>([]);
  const [loaded, setLoaded] = useState(false),
    [loadingDocument, setLoadingDocument] = useState(false);
  const [selected, setSelected] = useState<Document | null>(null);
  const [draft, setDraft] = useState<Draft>(() => {
    const saved = remembered<Draft>("draft");
    return saved &&
      typeof saved.text === "string" &&
      typeof saved.title === "string" &&
      typeof saved.audience === "string"
      ? saved
      : empty();
  });
  const [pending, setPending] = useState<Pending | null>(() => remembered<Pending>("pending"));
  const [editing, setEditing] = useState(() => !!draft.title || !!draft.text);
  const [busy, setBusy] = useState(false),
    [importing, setImporting] = useState(false),
    [error, setError] = useState(""),
    [notice, setNotice] = useState("");
  const [reviewed, setReviewed] = useState(false);
  const [deleteConfirmed, setDeleteConfirmed] = useState(false);
  const [retention, setRetention] = useState<Retention | null>(null),
    [retentionDays, setRetentionDays] = useState(0),
    [retentionConfirmed, setRetentionConfirmed] = useState(false),
    [retentionEditing, setRetentionEditing] = useState(false);
  const [query, setQuery] = useState(""),
    [employee, setEmployee] = useState(""),
    [retrieval, setRetrieval] = useState<Retrieval | null>(null);
  const owner = !role || role === "owner",
    locked = useRef(false),
    selection = useRef(0),
    libraryRequest = useRef(0),
    formVersion = useRef(0);
  const importSequence = useRef(0);
  const [drive, setDrive] = useState<DriveOptions | null>(null),
    [driveError, setDriveError] = useState(""),
    [driveId, setDriveId] = useState("");
  async function loadDrive() {
    setDriveError("");
    try {
      const options = await api<DriveOptions>("/company/documents/drive");
      setDrive(options);
      setDriveId((id) => (options.fileIds.includes(id) ? id : options.fileIds[0] || ""));
    } catch (e) {
      setDrive(null);
      setDriveError((e as Error).message);
    }
  }
  useEffect(() => {
    if (owner) void loadDrive();
  }, [owner]);
  useEffect(
    () => () => {
      importSequence.current++;
    },
    [],
  );
  const bytes = new TextEncoder().encode(draft.text).length;
  function persist(key: string, value: unknown) {
    try {
      value === null
        ? sessionStorage.removeItem(prefix + key)
        : sessionStorage.setItem(prefix + key, JSON.stringify(value));
    } catch {
      setNotice("This browser cannot retain unfinished work after closing. Save your draft before leaving.");
    }
  }
  function change(next: Draft) {
    formVersion.current++;
    setDraft(next);
    persist("draft", next);
  }
  function rememberPending(next: Pending | null) {
    setPending(next);
    persist("pending", next);
  }
  async function refresh() {
    const request = ++libraryRequest.current;
    try {
      const rows = await api<Document[]>("/company/documents");
      // Yesterday's guest list must not put a deleted document back on today's shelf.
      if (request === libraryRequest.current) {
        setDocuments(rows);
        setSelected((current) => (current && !rows.some((d) => d.id === current.id) ? null : current));
      }
    } catch (e) {
      if (request === libraryRequest.current) throw e;
    } finally {
      if (request === libraryRequest.current) setLoaded(true);
    }
  }
  useEffect(() => {
    void refresh().catch((e) => setError(e.message));
    void loadRetention().catch((e) => setError(e.message));
    return () => {
      selection.current++;
      libraryRequest.current++;
    };
  }, []);
  useEffect(() => {
    if (selected) document.querySelector<HTMLElement>(".co-document-review")?.scrollIntoView({ block: "start" });
  }, [selected?.id, selected?.version]);
  async function open(id: string) {
    const attempt = ++selection.current;
    setError("");
    setSelected(null);
    setLoadingDocument(true);
    try {
      const doc = await api<Document>("/company/documents/" + encodeURIComponent(id));
      if (attempt === selection.current) {
        setSelected(doc.state === "deleted" ? null : doc);
        if (doc.state === "deleted") {
          setNotice(deletionNotice(doc));
          if (draft.id === doc.id) {
            change(empty());
            setEditing(false);
          }
          await refresh();
        }
        setReviewed(false);
        setDeleteConfirmed(false);
      }
    } catch (e) {
      if (attempt === selection.current) setError((e as Error).message);
    } finally {
      if (attempt === selection.current) setLoadingDocument(false);
    }
  }
  function audience(doc: Pick<Document, "employeeId" | "departmentId">) {
    return doc.employeeId
      ? "Employee: " + (snapshot.data.employees.find((e) => e.id === doc.employeeId)?.name || "Unavailable employee")
      : doc.departmentId
        ? "Department: " +
          (snapshot.data.departments.find((d) => d.id === doc.departmentId)?.name || "Unavailable department")
        : "All employees";
  }
  async function loadRetention() {
    const policy = await api<Retention>("/company/documents/retention");
    setRetention(policy);
    setRetentionDays(policy.discardedDays);
    setRetentionConfirmed(false);
    setRetentionEditing(false);
  }
  async function finish(result: Document | Retention, receipt: Pending) {
    selection.current++;
    setLoadingDocument(false);
    setReviewed(false);
    setDeleteConfirmed(false);
    setRetrieval(null);
    rememberPending(null);
    if (receipt.retention) {
      setSelected(null);
      setNotice("Discarded-draft retention saved. Any already expired discarded drafts have been deleted.");
      await loadRetention();
      if (draft.id && (await api<Document>("/company/documents/" + encodeURIComponent(draft.id))).state === "deleted") {
        change(empty());
        setEditing(false);
      }
      await refresh();
      return;
    }
    const doc = result as Document;
    setSelected(doc.state === "deleted" ? null : doc);
    if (
      (receipt.saving && JSON.stringify(draft) === receipt.fingerprint) ||
      (doc.state === "deleted" && draft.id === doc.id)
    ) {
      change(empty());
      setEditing(false);
    }
    setNotice(
      doc.state === "draft"
        ? "Document draft saved. Review its text and audience before approval."
        : doc.state === "deleted"
          ? deletionNotice(doc)
          : "Document " + doc.state + ".",
    );
    await refresh().catch(() => setNotice("The change is saved. Refresh the document list to see its latest status."));
  }
  async function mutate(action: string, fields: Record<string, unknown>) {
    if (locked.current || pending) return;
    locked.current = true;
    setBusy(true);
    setError("");
    setNotice("");
    let receipt: Pending | null = null;
    try {
      const result = await command(
        action,
        fields,
        (id) => {
          receipt = {
            id,
            fingerprint: JSON.stringify(draft),
            saving: action === "document.save",
            retention: action === "document.retention",
          };
          rememberPending(receipt);
        },
        action === "document.save" ? "/company/documents/save" : "/company/commands",
      );
      if (receipt) await finish(result as Document, receipt);
    } catch (e) {
      if (!(e instanceof UnconfirmedCompanyOperation)) rememberPending(null);
      setError((e as Error).message);
    } finally {
      locked.current = false;
      setBusy(false);
    }
  }
  async function recover() {
    if (!pending || locked.current) return;
    locked.current = true;
    setBusy(true);
    setError("");
    try {
      await finish((await reconcileCompanyOperation(pending.id)) as Document, pending);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      locked.current = false;
      setBusy(false);
    }
  }
  function copy(doc: Document) {
    setEditing(true);
    change({
      title: doc.title,
      text: doc.text || "",
      audience: doc.employeeId
        ? "employee:" + doc.employeeId
        : doc.departmentId
          ? "department:" + doc.departmentId
          : "company",
      sourceName: doc.sourceName || undefined,
      sourceReceipt: doc.origin?.receipt,
      ...(doc.state === "draft"
        ? { id: doc.id, version: doc.version, replacesId: doc.replacesId || undefined }
        : { replacesId: doc.id }),
    });
    setNotice("Document loaded into the editor. Saving creates a draft, not an approval.");
    requestAnimationFrame(() =>
      document.querySelector<HTMLElement>(".co-document-editor")?.scrollIntoView({ block: "start" }),
    );
  }
  async function importDrive() {
    if (locked.current || pending || importing || !driveId) return;
    const version = formVersion.current;
    const sequence = ++importSequence.current;
    setError("");
    setNotice("Reading the selected Drive file…");
    setImporting(true);
    try {
      const preview = await api<DocumentImport>(
        "/company/documents/drive-preview",
        "POST",
        { fileId: driveId },
        { timeoutMs: 35000 },
      );
      if (sequence !== importSequence.current) return;
      if (locked.current || version !== formVersion.current) {
        setNotice("The editor changed during import. Preview the Drive file again when ready.");
        return;
      }
      change({
        ...draft,
        title: draft.title || preview.title,
        text: preview.text,
        sourceName: preview.sourceName,
        sourceReceipt: preview.sourceReceipt,
      });
      setNotice("Drive text loaded for review. It has not been saved or approved.");
    } catch (e) {
      if (sequence !== importSequence.current) return;
      setNotice("");
      setError(
        e instanceof ApiError && e.status === 0
          ? "Drive preview did not finish. Your draft is unchanged; preview the file again."
          : (e as Error).message,
      );
    } finally {
      if (sequence === importSequence.current) setImporting(false);
    }
  }
  async function importFile(file?: File) {
    if (!file || locked.current || pending) return;
    const version = formVersion.current;
    const sequence = ++importSequence.current;
    const word = /\.docx$/i.test(file.name);
    const pdf = /\.pdf$/i.test(file.name);
    const format = pdf ? "PDF" : "Word";
    setError("");
    setNotice(word || pdf ? `Reading ${format} document text…` : "Reading text file…");
    setImporting(true);
    try {
      if (!/\.(txt|md|docx|pdf)$/i.test(file.name))
        throw new Error(
          "Choose UTF-8 .txt or .md, Word .docx, or PDF. Older .doc and macro-enabled Word files are not supported.",
        );
      if (file.size > (word || pdf ? 2 * 1024 * 1024 : 65536))
        throw new Error(
          word || pdf ? `Choose a ${format} file of at most 2 MiB.` : "Choose a UTF-8 text file of at most 64 KiB.",
        );
      const buffer = await file.arrayBuffer();
      let text: string;
      if (word || pdf) {
        const data = new Uint8Array(buffer);
        let binary = "";
        for (let start = 0; start < data.length; start += 16384)
          binary += String.fromCharCode(...data.subarray(start, start + 16384));
        const preview = await api<{ text: string; notice: string }>(
          pdf ? "/company/documents/extract-pdf" : "/company/documents/extract-word",
          "POST",
          { name: file.name, contentBase64: btoa(binary) },
          { timeoutMs: 15000 },
        );
        text = preview.text;
      } else text = new TextDecoder("utf-8", { fatal: true }).decode(buffer);
      if (sequence !== importSequence.current) return;
      if (locked.current) {
        setNotice("A save started during import. Choose the file again after it finishes.");
        return;
      }
      if (version !== formVersion.current) {
        setNotice("The editor changed during import. Choose the file again when ready.");
        return;
      }
      if (/\u0000/.test(text)) throw new Error("This file is not supported plain text.");
      change({
        ...draft,
        title: draft.title || file.name.replace(/\.(txt|md|docx|pdf)$/i, ""),
        text,
        sourceName: file.name,
        sourceReceipt: undefined,
      });
      setNotice(
        word || pdf
          ? `${format} text loaded for review. Check the extraction notes below. It has not been saved or approved.`
          : "File loaded for review. It has not been saved or approved.",
      );
    } catch (e) {
      if (sequence !== importSequence.current) return;
      setNotice("");
      setError(
        (word || pdf) && e instanceof ApiError && e.status === 0
          ? `${format} import did not finish. Your draft is unchanged; select the file again.`
          : e instanceof TypeError && !word && !pdf
            ? "The file is not valid UTF-8 text. Export it as UTF-8 and try again."
            : (e as Error).message,
      );
    } finally {
      if (sequence === importSequence.current) setImporting(false);
    }
  }
  async function preview() {
    if (locked.current) return;
    locked.current = true;
    setBusy(true);
    setError("");
    try {
      setRetrieval(
        await api<Retrieval>("/company/documents/retrieve", "POST", {
          query,
          employeeId: employee || null,
          companyOnly: !employee,
        }),
      );
    } catch (e) {
      setError((e as Error).message);
    } finally {
      locked.current = false;
      setBusy(false);
    }
  }
  return (
    <section className="co-documents" aria-label="Company documents">
      <h2>Documents your team can use</h2>
      <p>
        Add brand guidelines, project notes or other reference material. Review the text and choose which employees may
        receive relevant excerpts. Company members can view this library; employee audiences control task context.
      </p>
      {error && (
        <p className="co-error" role="alert">
          {error}
        </p>
      )}
      {notice && <p role="status">{notice}</p>}
      {pending && (
        <aside>
          <p>A document change needs confirmation. It will not be repeated.</p>
          <button disabled={busy} onClick={() => void recover()}>
            Check document receipt
          </button>
        </aside>
      )}
      {owner && !editing && (
        <button className="co-primary" disabled={busy || !!pending} onClick={() => setEditing(true)}>
          Add a document
        </button>
      )}
      {owner && editing && (
        <form
          className="co-document-editor"
          onSubmit={(event) => {
            event.preventDefault();
            if (importing) return;
            const [scope, target] = draft.audience.split(":");
            void mutate("document.save", {
              id: draft.id,
              expectedVersion: draft.version || 0,
              body: JSON.stringify({
                title: draft.title,
                text: draft.text,
                sourceName: draft.sourceName,
                sourceReceipt: draft.sourceReceipt,
                replacesId: draft.replacesId,
                departmentId: scope === "department" ? target : null,
                employeeId: scope === "employee" ? target : null,
              }),
            });
          }}
        >
          <h3>{draft.id ? "Edit saved draft" : draft.replacesId ? "Revised document draft" : "Add a document"}</h3>
          <fieldset disabled={busy || !!pending}>
            <label>
              Document title
              <input
                required
                maxLength={160}
                value={draft.title}
                onChange={(e) => change({ ...draft, title: e.target.value })}
              />
            </label>
            <label>
              Available to employees
              <select value={draft.audience} onChange={(e) => change({ ...draft, audience: e.target.value })}>
                <option value="company">All employees</option>
                {snapshot.data.departments.map((d) => (
                  <option key={d.id} value={"department:" + d.id}>
                    Department: {d.name}
                  </option>
                ))}
                {snapshot.data.employees
                  .filter((e) => e.state !== "retired")
                  .map((e) => (
                    <option key={e.id} value={"employee:" + e.id}>
                      Employee: {e.name}
                    </option>
                  ))}
              </select>
            </label>
            <label>
              Import a document
              <input
                type="file"
                accept=".txt,.md,.docx,.pdf,application/pdf,text/plain,text/markdown,application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                disabled={importing}
                onChange={(e) => {
                  void importFile(e.target.files?.[0]);
                  e.target.value = "";
                }}
              />
            </label>
            <details className="co-document-formats">
              <summary>File types and limits</summary>
              <p>
                UTF-8 text or Markdown up to 64 KiB. Word .docx or PDF up to 2 MiB, with at most 64 KiB of extracted
                text. PDFs may have up to 50 pages; scanned text needs OCR first.
              </p>
            </details>
            {draft.sourceName && <small className="co-document-source">Source filename: {draft.sourceName}</small>}
            {draft.sourceName?.toLowerCase().endsWith(".docx") && <WordImportNotice />}
            {draft.sourceName?.toLowerCase().endsWith(".pdf") && <PdfImportNotice />}
            <details className="co-drive-import">
              <summary>Import from Google Drive</summary>
              <p>
                Read one allowed file into this editor. Only saved, separately approved text becomes available to
                employees. Imported copies are editable snapshots: Drive changes or disconnection do not update or
                remove them.
              </p>
              {driveError && <p role="alert">{driveError}</p>}
              {drive && <p>{drive.reason}</p>}
              {drive?.ready && (
                <>
                  <label>
                    Allowed Drive file
                    <select value={driveId} disabled={importing} onChange={(e) => setDriveId(e.target.value)}>
                      {drive.fileIds.map((id) => (
                        <option key={id} value={id}>
                          {id}
                        </option>
                      ))}
                    </select>
                  </label>
                  <button type="button" disabled={importing || !driveId} onClick={() => void importDrive()}>
                    Import selected file text
                  </button>
                  {draft.text && (
                    <small>
                      Import replaces the text in this editor. Newer edits made during the read are preserved.
                    </small>
                  )}
                </>
              )}
              <button type="button" disabled={importing} onClick={() => void loadDrive()}>
                Refresh Drive connection
              </button>
              <a
                href={
                  "/?view=departments&section=integrations&company=" + encodeURIComponent(company() || "local-company")
                }
              >
                Manage Drive connection and allowed files
              </a>
            </details>
            <label>
              Document text
              <textarea
                required
                rows={12}
                maxLength={65536}
                value={draft.text}
                onChange={(e) => change({ ...draft, text: e.target.value })}
              />
            </label>
            <small>
              {bytes.toLocaleString()} / 65,536 bytes · Saving keeps this private draft out of employee context.
            </small>
            <div className="co-actions">
              <button
                className="co-primary"
                disabled={importing || !draft.title.trim() || !draft.text.trim() || bytes > 65536}
              >
                Save document draft
              </button>
              <button type="button" onClick={() => change(empty())}>
                Clear editor
              </button>
            </div>
          </fieldset>
        </form>
      )}
      <section className="co-document-library">
        <h3>Saved documents</h3>
        <button disabled={busy} onClick={() => void refresh().catch((e) => setError(e.message))}>
          Refresh documents
        </button>
        <small>
          {documents.length} / 50 records · {Math.ceil(documents.reduce((n, d) => n + (d.bytes || 0), 0) / 1024)} /
          2,048 KiB stored
        </small>
        {!loaded && <p role="status">Loading documents…</p>}
        {loaded && !documents.length && !error && <p>No documents yet. Start with a short brand or project guide.</p>}
        <ul>
          {documents
            .slice()
            .reverse()
            .map((d) => (
              <li key={d.id}>
                <button disabled={busy} onClick={() => void open(d.id)}>
                  <b>{d.title}</b>
                  <span>
                    {d.state} · {audience(d)} · version {d.version}
                  </span>
                </button>
              </li>
            ))}
        </ul>
      </section>
      {loadingDocument && <p role="status">Loading the saved document…</p>}
      {selected && (
        <section className="co-document-review" aria-label="Review document">
          <h3>{selected.title}</h3>
          <p>
            {selected.state} · version {selected.version} · {audience(selected)}
          </p>
          <p>
            {selected.sourceName || "Pasted text"} · Saved {new Date(selected.updatedAt).toLocaleString()}
          </p>
          <DocumentSourceReview
            document={selected}
            owner={owner}
            blocked={busy || importing || !!pending || !!draft.title || !!draft.text}
            onRevision={(preview) => {
              if (busy || importing || pending || draft.title || draft.text) return;
              change({
                title: selected.title,
                text: preview.text,
                sourceName: preview.sourceName,
                sourceReceipt: preview.sourceReceipt,
                audience: selected.employeeId
                  ? `employee:${selected.employeeId}`
                  : selected.departmentId
                    ? `department:${selected.departmentId}`
                    : "company",
                replacesId: selected.id,
              });
              setEditing(true);
              setNotice(
                "Latest source copied into a revised draft. Review owner corrections, then save; the current document is unchanged.",
              );
              requestAnimationFrame(() =>
                document.querySelector(".co-document-editor")?.scrollIntoView({ block: "start" }),
              );
            }}
          />
          <pre>{selected.text}</pre>
          {selected.sourceName?.toLowerCase().endsWith(".docx") && <WordImportNotice />}
          {selected.sourceName?.toLowerCase().endsWith(".pdf") && <PdfImportNotice />}
          {selected.replacesId && (
            <p>This revises an earlier document. Revoke that version explicitly before approving this one.</p>
          )}
          {owner && !pending && (
            <div>
              {selected.state === "draft" && (
                <>
                  <label className="co-document-check">
                    <input
                      type="checkbox"
                      checked={reviewed}
                      disabled={busy}
                      onChange={(e) => setReviewed(e.target.checked)}
                    />
                    <span>
                      I reviewed this text and employee audience. Relevant excerpts may be sent to the configured model
                      for authorized tasks.
                    </span>
                  </label>
                  <button
                    className="co-primary"
                    disabled={busy || !reviewed}
                    onClick={() =>
                      void mutate("document.review", {
                        id: selected.id,
                        expectedVersion: selected.version,
                        digest: selected.digest,
                        decision: "approve",
                      })
                    }
                  >
                    Approve document for this audience
                  </button>
                  <button
                    disabled={busy}
                    onClick={() =>
                      void mutate("document.review", {
                        id: selected.id,
                        expectedVersion: selected.version,
                        digest: selected.digest,
                        decision: "discard",
                      })
                    }
                  >
                    Discard draft
                  </button>
                </>
              )}
              {selected.state === "approved" && (
                <button
                  disabled={busy}
                  onClick={() =>
                    void mutate("document.review", {
                      id: selected.id,
                      expectedVersion: selected.version,
                      digest: selected.digest,
                      decision: "revoke",
                    })
                  }
                >
                  Revoke document access
                </button>
              )}
              {["draft", "approved", "revoked"].includes(selected.state) && (
                <button disabled={busy} onClick={() => copy(selected)}>
                  {selected.state === "draft" ? "Edit this draft" : "Create a revised draft"}
                </button>
              )}
              {["draft", "discarded"].includes(selected.state) && (
                <details className="co-document-delete">
                  <summary>Delete this unapproved document</summary>
                  <p>
                    Removes this document’s saved text, title, filename and recovery copies. It has never been approved
                    for employee use. This cannot be undone in the workspace.
                  </p>
                  <label className="co-document-check">
                    <input
                      type="checkbox"
                      checked={deleteConfirmed}
                      disabled={busy}
                      onChange={(e) => setDeleteConfirmed(e.target.checked)}
                    />
                    <span>I understand that this unapproved document will be deleted.</span>
                  </label>
                  <button
                    disabled={busy || !deleteConfirmed}
                    onClick={() =>
                      void mutate("document.delete", {
                        id: selected.id,
                        expectedVersion: selected.version,
                        digest: selected.digest,
                        decision: "delete-unapproved",
                      })
                    }
                  >
                    Permanently delete unapproved document
                  </button>
                </details>
              )}
              {selected.state === "revoked" && (
                <details className="co-document-delete co-document-source-delete">
                  <summary>Delete library copy</summary>
                  <p>
                    Removes the saved text, title, filename and document recovery copies. Access is already revoked.
                    This cannot be undone here.
                  </p>
                  <p>
                    Earlier task excerpts and results, other drafts, original files, open browsers, downloads, backups
                    and database recovery files remain. Copies sent to a provider cannot be recalled here. This does not
                    erase the document everywhere.
                  </p>
                  <label className="co-document-check">
                    <input
                      type="checkbox"
                      checked={deleteConfirmed}
                      disabled={busy}
                      onChange={(e) => setDeleteConfirmed(e.target.checked)}
                    />
                    <span>Delete this revoked library copy. I understand that earlier copies remain.</span>
                  </label>
                  <button
                    disabled={busy || !deleteConfirmed}
                    onClick={() =>
                      void mutate("document.source.delete", {
                        id: selected.id,
                        expectedVersion: selected.version,
                        digest: selected.digest,
                        decision: "delete-revoked-source",
                      })
                    }
                  >
                    Permanently delete library copy
                  </button>
                </details>
              )}
            </div>
          )}
          <small>
            {selected.state === "approved"
              ? "Revoking access stops new use and invalidates prepared work using this version. Earlier task copies remain."
              : selected.state === "revoked"
                ? "Access is revoked. Earlier task copies remain separate from this library source."
                : "Discarding keeps a draft until you delete it or its retention period expires."}
          </small>
        </section>
      )}
      <section className="co-document-retention" aria-label="Document retention">
        <h3>Retention for discarded drafts</h3>
        <button disabled={busy || !!pending} onClick={() => void loadRetention().catch((e) => setError(e.message))}>
          Refresh retention setting
        </button>
        <p>
          Delete only never-approved documents you have discarded. Active drafts and previously approved documents are
          kept. Automatic deletion is checked every minute while the workspace is running.
        </p>
        {!retention && <p>Loading retention settings…</p>}
        {retention && (
          <p className="co-retention-current">
            <strong>Current rule: </strong>
            {retention.discardedDays
              ? `Discarded drafts are deleted after ${retention.discardedDays} days.`
              : "Discarded drafts are kept until an owner deletes them."}
          </p>
        )}
        {retention && owner && !retentionEditing && (
          <button
            disabled={busy || !!pending}
            onClick={() => {
              setRetentionDays(retention.discardedDays);
              setRetentionConfirmed(false);
              setRetentionEditing(true);
            }}
          >
            Change retention setting
          </button>
        )}
        {retention && owner && retentionEditing && (
          <form
            onSubmit={(event) => {
              event.preventDefault();
              void mutate("document.retention", {
                expectedVersion: retention.version,
                body: JSON.stringify({ discardedDays: retentionDays }),
                decision: "apply-retention",
              });
            }}
          >
            <fieldset disabled={busy || !!pending}>
              <label>
                Keep discarded drafts
                <select
                  value={retentionDays}
                  onChange={(e) => {
                    setRetentionDays(Number(e.target.value));
                    setRetentionConfirmed(false);
                  }}
                >
                  <option value={0}>Until I delete them</option>
                  <option value={7}>7 days after discarding</option>
                  <option value={30}>30 days after discarding</option>
                  <option value={90}>90 days after discarding</option>
                </select>
              </label>
              {retentionDays > 0 && (
                <p>
                  Saving this setting will also delete{" "}
                  {
                    documents.filter(
                      (d) =>
                        d.state === "discarded" &&
                        new Date(d.updatedAt).getTime() <= Date.now() - retentionDays * 86400000,
                    ).length
                  }{" "}
                  currently expired discarded drafts. The server checks their age again when you save.
                </p>
              )}
              <label className="co-document-check">
                <input
                  type="checkbox"
                  checked={retentionConfirmed}
                  onChange={(e) => setRetentionConfirmed(e.target.checked)}
                />
                <span>
                  {retentionDays
                    ? "Apply this rule to existing and future discarded drafts, including deletion of those already expired."
                    : "Keep remaining discarded drafts until I delete them. This does not restore deleted content."}
                </span>
              </label>
              <button disabled={!retentionConfirmed || retentionDays === retention.discardedDays}>
                Save retention setting
              </button>
              <button type="button" onClick={() => setRetentionEditing(false)}>
                Cancel changes
              </button>
            </fieldset>
          </form>
        )}
        <details className="co-deletion-scope">
          <summary>What deletion removes and keeps</summary>
          <small>
            Deletion removes content from the active document store and its saved operation results. IDs, hashes,
            deletion times and audit events remain. Existing backups, database recovery files, imported originals and
            copies in other browsers are not erased by this control. Backup expiry is managed by the workspace operator.
          </small>
        </details>
      </section>
      <section className="co-document-search">
        <h3>Preview what an employee can find</h3>
        <p>Uses keyword matching over approved documents. This preview makes no model request.</p>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void preview();
          }}
        >
          <label>
            Employee context
            <select
              value={employee}
              onChange={(e) => {
                setEmployee(e.target.value);
                setRetrieval(null);
              }}
            >
              <option value="">Company conversation only</option>
              {snapshot.data.employees
                .filter((e) => e.state !== "retired")
                .map((e) => (
                  <option key={e.id} value={e.id}>
                    {e.name}
                  </option>
                ))}
            </select>
          </label>
          <label>
            Task or topic
            <input
              required
              maxLength={4000}
              value={query}
              onChange={(e) => {
                setQuery(e.target.value);
                setRetrieval(null);
              }}
            />
          </label>
          <button disabled={busy || !query.trim()}>Find relevant excerpts</button>
        </form>
        {retrieval && (
          <div className="co-document-results" role="status">
            <p>{retrieval.notice}</p>
            {retrieval.excerpts.map((e) => (
              <article key={e.documentId + ":" + e.chunk}>
                <h4>
                  {e.title} · version {e.version}
                </h4>
                <small>Approved {new Date(e.approvedAt).toLocaleString()}</small>
                <blockquote>{e.text}</blockquote>
                <button disabled={busy} onClick={() => void open(e.documentId)}>
                  Review full document
                </button>
              </article>
            ))}
          </div>
        )}
      </section>
    </section>
  );
}
