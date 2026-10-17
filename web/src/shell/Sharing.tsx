import { useCallback, useEffect, useState } from "react";
import { api } from "./api";
import { WorkLogGlance } from "./WorkLog";

type Sharing = {
  settings: { bossOrganizations: string[]; hiddenProjects: string[]; ownerName?: string | null };
  sharedWithBoss: { name: string; repository: string }[];
};

/** What a boss may see: company organizations only, minus hidden projects, with a preview of exactly what they'd get. */
export function SharingSection() {
  const [data, setData] = useState<Sharing | null>(null),
    [orgs, setOrgs] = useState(""),
    [hidden, setHidden] = useState(""),
    [name, setName] = useState(""),
    [preview, setPreview] = useState<string | null>(null),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const load = useCallback(async () => {
    try {
      const value = await api<Sharing>("/sharing");
      setData(value);
      setOrgs(value.settings.bossOrganizations.join(", "));
      setHidden(value.settings.hiddenProjects.join(", "));
      setName(value.settings.ownerName ?? "");
    } catch (cause) {
      setError((cause as Error).message);
    }
  }, []);
  useEffect(() => {
    void load();
  }, [load]);
  const list = (text: string) =>
    text
      .split(",")
      .map((value) => value.trim())
      .filter(Boolean);
  async function save() {
    setBusy(true);
    setError("");
    try {
      await api("/sharing", "PUT", {
        bossOrganizations: list(orgs),
        hiddenProjects: list(hidden),
        ownerName: name.trim() || null,
      });
      setPreview(null);
      await load();
    } catch (cause) {
      setError((cause as Error).message);
    } finally {
      setBusy(false);
    }
  }
  async function show() {
    setBusy(true);
    setError("");
    try {
      const value = await api<{ context: string | null }>(
        "/context?audience=boss&q=" + encodeURIComponent("what has been done this week"),
      );
      setPreview(value.context ?? "Nothing would be shared right now.");
    } catch (cause) {
      setError((cause as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <section className="fe-section fe-stack">
      <h2>What your boss can see</h2>
      <p className="fe-muted">
        Bosses get a sanitized log: projects from these organizations only, which days you worked on them, and pull
        requests you opened or merged. Never session titles, files, commands, messages or personal repositories. Deeper
        questions are pointed back to you.
      </p>
      <label>
        Your name on the boss panel
        <input value={name} maxLength={80} onChange={(event) => setName(event.target.value)} placeholder="Mark Hall" />
      </label>
      <label>
        Company organizations (comma-separated)
        <input value={orgs} maxLength={500} onChange={(event) => setOrgs(event.target.value)} placeholder="Example" />
      </label>
      <label>
        Hidden projects (comma-separated names or owner/repo)
        <input value={hidden} maxLength={2000} onChange={(event) => setHidden(event.target.value)} />
      </label>
      <div className="dev-shift-actions">
        <button className="primary" disabled={busy} onClick={() => void save()}>
          Save
        </button>
        <button className="fe-ghost" disabled={busy} onClick={() => void show()}>
          Preview what your boss sees
        </button>
      </div>
      {data && (
        <p className="fe-muted">
          Shared now:{" "}
          {data.sharedWithBoss.length ? data.sharedWithBoss.map((project) => project.name).join(", ") : "nothing"}.
        </p>
      )}
      {preview && <pre className="dev-memory-long">{preview}</pre>}
      {error && (
        <p className="fe-alert" role="alert">
          {error}
        </p>
      )}
      <WorkLogGlance preview onForbidden={() => {}} />
    </section>
  );
}
