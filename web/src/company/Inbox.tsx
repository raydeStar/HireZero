import { memberStorage as sessionStorage } from "../lib/api";
import { useEffect, useRef, useState } from "react";
import { Bell } from "lucide-react";
import { api, company } from "../lib/api";
import { command, reconcileCompanyOperation, UnconfirmedCompanyOperation, type Work } from "./api";
import "./inbox.css";

type Notice = {
  id: string;
  sequence: number;
  category: string;
  title: string;
  detail: string;
  destination: string;
  targetId: string | null;
  at: string;
};
type Preferences = {
  emailEnabled: boolean;
  completed: boolean;
  needsAttention: boolean;
  dailyLimit: number;
  version: number;
  until: string | null;
};
type Delivery = { id: string; state: string; createdAt: string; reason: string | null };
type InboxData = {
  items: { notice: Notice; read: boolean }[];
  nextBefore: number | null;
  unread: number;
  preferences: Preferences;
  email: { available: boolean; eligible: boolean; address: string | null; serviceDigest: string | null };
  deliveries: Delivery[];
};

export function InboxButton({ version, open }: { version: number; open: () => void }) {
  const [unread, setUnread] = useState<number | null>(null);
  useEffect(() => {
    let alive = true;
    const load = () =>
      void api<{ unread: number }>("/company/notifications/unread")
        .then((r) => {
          if (alive) setUnread(r.unread);
        })
        .catch(() => {
          if (alive) setUnread(null);
        });
    load();
    const timer = window.setInterval(load, 20000);
    const countChanged = (event: Event) => {
      if (alive) setUnread((event as CustomEvent<number>).detail);
    };
    window.addEventListener("hirezero-inbox-changed", load);
    window.addEventListener("hirezero-inbox-count", countChanged);
    return () => {
      alive = false;
      clearInterval(timer);
      window.removeEventListener("hirezero-inbox-changed", load);
      window.removeEventListener("hirezero-inbox-count", countChanged);
    };
  }, [version]);
  return (
    <button
      className="fe-icon-button co-inbox-toggle"
      onClick={open}
      title="Inbox"
      aria-label={`Open inbox${unread === null ? ", count unavailable" : `, ${unread} unread`}`}
    >
      <Bell size={17} />
      {unread !== null && unread > 0 && <span className="fe-count attn">{unread > 99 ? "99+" : unread}</span>}
    </button>
  );
}

export function Inbox({
  role,
  refresh,
  onOpenWork,
  onSources,
  onSchedule,
  onMemory,
  onPlan,
  onMeeting,
}: {
  role?: string;
  refresh: () => Promise<void>;
  onOpenWork: (work: Work) => void;
  onSources: () => void;
  onSchedule: (id: string) => void;
  onMemory: (id: string) => void;
  onPlan: (id: string) => void;
  onMeeting: (id: string) => void;
}) {
  const receiptKey = `hirezero-inbox-receipt-v1:${company() || "local-company"}`;
  const [pending, setPending] = useState(() => {
    try {
      return sessionStorage.getItem(receiptKey) || "";
    } catch {
      return "";
    }
  });
  const [data, setData] = useState<InboxData | null>(null),
    [error, setError] = useState(""),
    [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false),
    [unreadOnly, setUnreadOnly] = useState(false),
    [before, setBefore] = useState<number | null>(null);
  const [form, setForm] = useState<Preferences | null>(null),
    [consent, setConsent] = useState(false);
  const dirty = useRef(false),
    digest = useRef<string | null>(null),
    epoch = useRef(0),
    lock = useRef(false);
  const owner = !role || role === "owner";
  async function load() {
    const generation = ++epoch.current;
    const next = await api<InboxData>(
      `/company/notifications?unread=${unreadOnly}${before ? `&before=${before}` : ""}`,
    );
    if (generation !== epoch.current) return;
    setData(next);
    window.dispatchEvent(new CustomEvent("hirezero-inbox-count", { detail: next.unread }));
    if (!dirty.current) {
      setForm(next.preferences);
      digest.current = next.email.serviceDigest;
    }
  }
  useEffect(() => {
    void load().catch((e) => setError((e as Error).message));
    return () => {
      epoch.current++;
    };
  }, [before, unreadOnly]);
  useEffect(() => {
    if (before || busy) return;
    const timer = window.setInterval(() => {
      void load().catch(() => setError("The inbox could not refresh. Your saved alerts remain available."));
    }, 10000);
    return () => clearInterval(timer);
  }, [before, unreadOnly, busy]);
  function remember(id: string) {
    setPending(id);
    try {
      id ? sessionStorage.setItem(receiptKey, id) : sessionStorage.removeItem(receiptKey);
    } catch {}
  }
  async function perform(action?: string, fields: Record<string, unknown> = {}) {
    if (lock.current) return;
    lock.current = true;
    setBusy(true);
    setError("");
    setNotice("");
    try {
      const result = pending ? await reconcileCompanyOperation(pending) : await command(action!, fields, remember);
      remember("");
      if (result && typeof result === "object" && "emailEnabled" in result) {
        dirty.current = false;
        setConsent(false);
      }
      await load();
      window.dispatchEvent(new Event("hirezero-inbox-changed"));
      setNotice(
        action === "notification.preferences" ? "Notification preferences saved. They apply to new alerts." : "Saved.",
      );
    } catch (e) {
      if (!(e instanceof UnconfirmedCompanyOperation) && !pending) remember("");
      setError((e as Error).message);
    } finally {
      lock.current = false;
      setBusy(false);
    }
  }
  async function open(item: Notice) {
    setError("");
    try {
      if (item.destination === "task" && item.targetId) {
        const result = await api<{ task: Work }>(`/company/tasks/${encodeURIComponent(item.targetId)}`);
        await refresh();
        onOpenWork(result.task);
      } else if (item.destination === "schedule" && item.targetId) onSchedule(item.targetId);
      else if (item.destination === "memory" && item.targetId) onMemory(item.targetId);
      else if (item.destination === "plan" && item.targetId) {
        await refresh();
        onPlan(item.targetId);
      } else if (item.destination === "meeting" && item.targetId) {
        await refresh();
        onMeeting(item.targetId);
      } else if (item.destination === "business") onSources();
      else throw new Error("This alert’s destination is unavailable. Refresh the inbox and try again.");
    } catch (e) {
      setError((e as Error).message);
    }
  }
  function edit(next: Preferences) {
    dirty.current = true;
    setForm(next);
    setConsent(false);
    setNotice("");
  }
  return (
    <section className="co-inbox" aria-label="Workspace inbox">
      <header>
        <p className="co-eyebrow">Keep track of the work</p>
        <h2>Your workspace inbox</h2>
        <p>Saved results and decisions that need attention. Reading an alert does not approve or change its work.</p>
      </header>
      {error && (
        <p role="alert" className="co-error">
          {error}
        </p>
      )}
      {notice && <p role="status">{notice}</p>}
      {pending && (
        <div role="status">
          <p>
            {busy
              ? "Saving your request…"
              : "The response was not confirmed. Check its receipt before making another change."}
          </p>
          <button disabled={busy} onClick={() => void perform()}>
            Check inbox receipt
          </button>
        </div>
      )}
      <div className="co-inbox-toolbar">
        <label>
          <input
            type="checkbox"
            checked={unreadOnly}
            onChange={(e) => {
              setBefore(null);
              setUnreadOnly(e.target.checked);
            }}
          />{" "}
          Unread only
        </label>
        <button
          disabled={busy}
          onClick={() => {
            setError("");
            void load().catch((e) => setError((e as Error).message));
          }}
        >
          Refresh inbox
        </button>
        {data && <span>{data.unread} unread</span>}
      </div>
      {!data ? (
        <p role="status">Loading your inbox…</p>
      ) : (
        <>
          {!data.items.length && (
            <p className="co-inbox-empty">
              {unreadOnly
                ? "You’re caught up. Switch off Unread only to revisit saved alerts."
                : "No alerts yet. Results and work needing attention will appear here."}
            </p>
          )}
          <ol className="co-inbox-items">
            {data.items.map(({ notice: item, read }) => (
              <li key={item.id} className={read ? "is-read" : "is-unread"}>
                <div>
                  <span className="co-eyebrow">
                    {item.category === "completed" ? "Result ready" : "Needs attention"}
                    {read ? " · Read" : " · Unread"}
                  </span>
                  <time dateTime={item.at}>{new Date(item.at).toLocaleString()}</time>
                </div>
                <h3>{item.title}</h3>
                <p>{item.detail}</p>
                <div className="co-inbox-actions">
                  <button className="co-primary" onClick={() => void open(item)}>
                    {item.destination === "task"
                      ? "Open result"
                      : item.destination === "schedule"
                        ? "Open schedule"
                        : item.destination === "memory"
                          ? "Review memory"
                          : "Review sources"}
                  </button>
                  <button
                    disabled={busy || !!pending}
                    onClick={() =>
                      void perform("notification.mark", { id: item.id, decision: read ? "unread" : "read" })
                    }
                  >
                    {read ? "Mark unread" : "Mark read"}
                  </button>
                </div>
              </li>
            ))}
          </ol>
          <div className="co-inbox-toolbar">
            {before && <button onClick={() => setBefore(null)}>Latest alerts</button>}
            {data.nextBefore && <button onClick={() => setBefore(data.nextBefore)}>Older alerts</button>}
          </div>
          {owner && form && (
            <details className="co-inbox-preferences">
              <summary>Email notification preferences</summary>
              <p>
                Optional generic emails tell you to check this inbox. They contain no business facts or work content.
              </p>
              {!data.email.available && (
                <p>Email delivery is not configured for this workspace. The inbox works without it.</p>
              )}
              {data.email.available && !data.email.eligible && (
                <p>Sign in with a verified email address as an owner to enable email notices.</p>
              )}
              {data.email.address && (
                <p>
                  Recipient: <strong>{data.email.address}</strong>
                </p>
              )}
              {data.preferences.until && (
                <p>
                  {new Date(data.preferences.until) > new Date()
                    ? "Current authorization expires"
                    : "Previous authorization expired"}
                  : {new Date(data.preferences.until).toLocaleString()}.
                </p>
              )}
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  void perform("notification.preferences", {
                    expectedVersion: form.version,
                    body: JSON.stringify({
                      emailEnabled: form.emailEnabled,
                      completed: form.completed,
                      needsAttention: form.needsAttention,
                      dailyLimit: form.dailyLimit,
                    }),
                    digest: digest.current,
                    decision: form.emailEnabled ? "authorize-notification-email" : "disable",
                  });
                }}
              >
                <fieldset disabled={busy || !!pending}>
                  <label>
                    <input
                      type="checkbox"
                      checked={form.emailEnabled}
                      disabled={!form.emailEnabled && (!data.email.available || !data.email.eligible)}
                      onChange={(e) => edit({ ...form, emailEnabled: e.target.checked })}
                    />{" "}
                    Email me about new work
                  </label>
                  <label>
                    <input
                      type="checkbox"
                      checked={form.completed}
                      onChange={(e) => edit({ ...form, completed: e.target.checked })}
                    />{" "}
                    Completed work
                  </label>
                  <label>
                    <input
                      type="checkbox"
                      checked={form.needsAttention}
                      onChange={(e) => edit({ ...form, needsAttention: e.target.checked })}
                    />{" "}
                    Work needing attention
                  </label>
                  <label>
                    Maximum emails per day (UTC)
                    <input
                      type="number"
                      min="1"
                      max="20"
                      required
                      value={form.dailyLimit}
                      onChange={(e) => edit({ ...form, dailyLimit: Number(e.target.value) })}
                    />
                  </label>
                  <p>
                    Authorization lasts 30 days. Only new alerts qualify; already-read alerts and alerts over 24 hours
                    old are skipped. The daily limit does not create a catch-up queue. Turning email off cannot recall a
                    message already sent.
                  </p>
                  {form.emailEnabled && (
                    <label>
                      <input type="checkbox" checked={consent} onChange={(e) => setConsent(e.target.checked)} /> I
                      authorize these generic notices to my verified address for 30 days, within this daily limit.
                    </label>
                  )}
                  <button
                    type="submit"
                    className="co-primary"
                    disabled={form.emailEnabled && (!consent || !data.email.available || !data.email.eligible)}
                  >
                    Save notification preferences
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      dirty.current = false;
                      setConsent(false);
                      setError("");
                      void load().catch((e) => setError((e as Error).message));
                    }}
                  >
                    Discard edits and reload preferences
                  </button>
                </fieldset>
              </form>
              <h3>Recent email attempts</h3>
              {!data.deliveries.length && <p>No emails attempted. Enabling email does not send old alerts.</p>}
              <ul className="co-inbox-deliveries">
                {data.deliveries.map((delivery) => (
                  <li key={delivery.id}>
                    <strong>
                      {(
                        {
                          accepted: "Accepted by mail server",
                          pending: "Waiting to send",
                          dispatching: "Sending",
                          unknown: "Delivery uncertain",
                          closed: "Kept in inbox",
                          skipped: "Kept in inbox",
                        } as Record<string, string>
                      )[delivery.state] || delivery.state}
                    </strong>{" "}
                    · {new Date(delivery.createdAt).toLocaleString()}
                    <p>{delivery.reason}</p>
                    {delivery.state === "unknown" && (
                      <>
                        <p>
                          Other emails are held until you acknowledge this outcome. The original message will never be
                          resent automatically.
                        </p>
                        <button
                          disabled={busy || !!pending}
                          onClick={() =>
                            void perform("notification.close", { id: delivery.id, decision: "keep-inbox-no-resend" })
                          }
                        >
                          Keep in inbox; do not resend
                        </button>
                      </>
                    )}
                  </li>
                ))}
              </ul>
            </details>
          )}
        </>
      )}
    </section>
  );
}
