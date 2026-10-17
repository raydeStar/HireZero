import { useState } from "react";
import { api } from "../../shell/api";
import type { RepositoryChoice, ReviewRepository } from "./github";
interface RepositoryConnectionFormProps {
  onConnected: (repository: ReviewRepository) => Promise<void>;
  onError: (cause: unknown) => void;
}
export function RepositoryConnectionForm({ onConnected, onError }: RepositoryConnectionFormProps) {
  const [name, setName] = useState(""),
    [root, setRoot] = useState(""),
    [choices, setChoices] = useState<RepositoryChoice[]>([]),
    [choice, setChoice] = useState(""),
    [busy, setBusy] = useState(false);
  async function resolve() {
    setBusy(true);
    try {
      const next = await api<RepositoryChoice[]>("/github/resolve?name=" + encodeURIComponent(name));
      setChoices(next);
      setChoice(next[0]?.name || "");
      if (!next.length) throw new Error("No matching repository. Give its owner/name.");
    } catch (cause) {
      onError(cause);
    } finally {
      setBusy(false);
    }
  }
  async function connect() {
    setBusy(true);
    try {
      await onConnected(
        await api<ReviewRepository>("/github/repositories", "POST", { name: choice, rootPath: root.trim() }),
      );
      setName("");
      setRoot("");
      setChoices([]);
    } catch (cause) {
      onError(cause);
    } finally {
      setBusy(false);
    }
  }
  return (
    <details className="fe-section">
      <summary>Connect a repository</summary>
      <form
        className="fe-form"
        onSubmit={(event) => {
          event.preventDefault();
          void resolve();
        }}
      >
        <label>
          Repository
          <input
            value={name}
            maxLength={100}
            required
            placeholder="owner/repository"
            onChange={(event) => {
              setName(event.target.value);
              setChoices([]);
            }}
          />
        </label>
        <button disabled={busy}>Find repository</button>
      </form>
      {choices.length > 0 && (
        <form
          className="fe-form"
          onSubmit={(event) => {
            event.preventDefault();
            void connect();
          }}
        >
          <label>
            Verified repository
            <select value={choice} onChange={(event) => setChoice(event.target.value)}>
              {choices.map((item) => (
                <option key={item.id} value={item.name}>
                  {item.name}
                </option>
              ))}
            </select>
          </label>
          <label>
            Local checkout (optional)
            <input
              value={root}
              maxLength={500}
              placeholder="C:\Users\…\my-repository"
              onChange={(event) => setRoot(event.target.value)}
            />
          </label>
          <p className="fe-muted">You can also ask in chat: “Please add owner/repository.”</p>
          <button className="primary" disabled={busy}>
            Confirm connection
          </button>
        </form>
      )}
    </details>
  );
}
