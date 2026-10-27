import { memberStorage as sessionStorage } from "../lib/api";
import { useEffect, useRef, useState } from "react";
import { api, ApiError, company } from "../lib/api";
import { command, reconcileCompanyOperation, UnconfirmedCompanyOperation } from "./api";
import { billingPrice } from "./billing-money";
import "./billing.css";

type Allowance = {
  jobs: number;
  tokens: number;
  computeJobs: number;
  computeSeconds: number;
  extendedScriptSeconds?: number | null;
};
type Plan = { id: string; name: string; price: { currency: string; unitAmount: number }; allowance: Allowance };
type Checkout = {
  id: string;
  version: number;
  digest: string;
  state: string;
  expiresAt: string;
  plan: Plan;
  receipt: { status: string; url: string | null; expiresAt: string } | null;
};
type Portal = { id: string; state: string; expiresAt: string; receipt: { url: string } | null };
type View = {
  configured: boolean;
  required: boolean;
  enabled: boolean;
  liveMode: boolean;
  plans: Plan[];
  checkout: Checkout | null;
  portal: Portal | null;
  allowance: Allowance | null;
  state: {
    status: string;
    version: number;
    customerId: string | null;
    verifiedAt: string | null;
    validUntil: string | null;
    periodEnd: string | null;
    cancelAtPeriodEnd: boolean;
  };
  usage: {
    jobs: number;
    tokens: number;
    heldTokens: number;
    carriedJobs: number;
    carriedTokens: number;
    resetsAt: string;
  };
  compute: { jobs: number; carriedJobs: number; allocatedSeconds: number; heldSeconds: number; carriedSeconds: number };
};
const number = (value: number) => value.toLocaleString();
const date = (value: string) => new Date(value).toLocaleString();
const terminal = new Set([
  "not-configured",
  "subscription-required",
  "checkout-failed",
  "expired",
  "canceled",
  "incomplete_expired",
]);
const statusText: Record<string, [string, string]> = {
  "subscription-required": ["Choose a plan", "Choose the allowance that fits the work you want your team to do."],
  queued: [
    "Preparing checkout",
    "Your approved plan is saved. Keep this page open or return later for the payment link.",
  ],
  dispatching: [
    "Preparing checkout",
    "The payment provider is preparing your checkout. You do not need to submit it again.",
  ],
  unconfirmed: [
    "Checkout needs confirmation",
    "The payment provider did not confirm its reply. We are checking the original request. Please do not create another subscription.",
  ],
  checking: ["Checking payment", "Your allowance will appear when the subscription and invoice are verified."],
  unverified: [
    "Complete your checkout",
    "Your subscription is not active yet. Continue to the secure payment page, then return here.",
  ],
  "checkout-open": [
    "Complete your checkout",
    "Your subscription is not active yet. Use the existing payment link to finish checkout.",
  ],
  active: ["Subscription active", "Your team can start work within the included allowance and your safety limits."],
  "payment-unverified": [
    "Payment needs attention",
    "Your payment could not be verified. Review payment details in the billing portal, then check status again.",
  ],
  past_due: [
    "Payment overdue",
    "Review the outstanding invoice in the billing portal. New work waits until payment is verified.",
  ],
  unpaid: ["Payment required", "Update your payment details in the billing portal before starting more work."],
  incomplete: ["Payment incomplete", "Finish the payment steps in Stripe, then check status again."],
  trialing: [
    "Payment not verified",
    "This subscription has no verified paid allowance yet. Review its details in the billing portal.",
  ],
  paused: ["Subscription paused", "Review your subscription in the billing portal. New work is paused."],
  canceled: [
    "Subscription canceled",
    "Your saved company and work remain available. Review a plan when you are ready to resume.",
  ],
  expired: ["Checkout expired", "The payment link has expired. Review a plan to prepare a new checkout."],
  incomplete_expired: [
    "Subscription was not activated",
    "The payment window ended. Review a plan to prepare a new checkout.",
  ],
  "checkout-failed": [
    "Checkout was not created",
    "The provider refused the request before creating checkout. Review a plan to try again.",
  ],
};

function Included({ allowance }: { allowance: Allowance }) {
  return (
    <dl className="co-billing-metrics">
      <div>
        <dt>AI jobs</dt>
        <dd>{number(allowance.jobs)}</dd>
      </div>
      <div>
        <dt>AI tokens</dt>
        <dd>{number(allowance.tokens)}</dd>
      </div>
      <div>
        <dt>Script / browser jobs</dt>
        <dd>{number(allowance.computeJobs)}</dd>
      </div>
      <div>
        <dt>Allocated compute</dt>
        <dd>{number(allowance.computeSeconds)} seconds</dd>
      </div>
      <div>
        <dt>Extended scripts</dt>
        <dd>
          {allowance.extendedScriptSeconds
            ? `Up to ${number(allowance.extendedScriptSeconds)} seconds per run, within included compute`
            : "Not included"}
        </dd>
      </div>
    </dl>
  );
}

export function Billing({ role, onUsage }: { role?: string; onUsage: () => void }) {
  const owner = !role || role === "owner";
  const scope = company() || "local-company";
  const reviewKey = `hirezero-billing-review:${scope}`,
    pendingKey = `hirezero-billing-operation:${scope}`;
  const [data, setData] = useState<View | null>(null),
    [proposal, setProposal] = useState<Checkout | null>(null);
  const [consent, setConsent] = useState(false),
    [busy, setBusy] = useState(false),
    [checkingPayment, setCheckingPayment] = useState(false),
    [error, setError] = useState("");
  const [pending, setPending] = useState(""),
    [notice, setNotice] = useState("");
  const [reload, setReload] = useState(0);
  const generation = useRef(0),
    lock = useRef(false),
    reviewPanel = useRef<HTMLElement>(null);
  function save(key: string, value: string) {
    try {
      value ? sessionStorage.setItem(key, value) : sessionStorage.removeItem(key);
    } catch {
      setNotice(
        "This browser cannot save the review locally. Your approved checkout is still saved with your company.",
      );
    }
  }
  function remember(id: string) {
    setPending(id);
    save(pendingKey, id);
  }
  async function load() {
    const current = ++generation.current;
    try {
      const next = await api<View>("/company/billing");
      if (generation.current !== current) return;
      setData(next);
      if (next.checkout && next.checkout.id === proposal?.id) {
        setProposal(null);
        setConsent(false);
        save(reviewKey, "");
      }
    } catch (e) {
      // A late courier must not contradict the newer receipt on the owner's desk.
      if (generation.current === current) throw e;
    }
  }
  async function checkPayment() {
    if (lock.current) return;
    lock.current = true;
    ++generation.current;
    setBusy(true);
    setCheckingPayment(true);
    setError("");
    setNotice("");
    try {
      await api("/company/billing/refresh", "POST", {}, { timeoutMs: 25_000 });
      setNotice("Payment status checked.");
    } catch (e) {
      setError((e as Error).message);
    } finally {
      await load().catch((e) => setError(e.message));
      lock.current = false;
      setBusy(false);
      setCheckingPayment(false);
    }
  }
  async function recover() {
    if (lock.current || !pending) return;
    lock.current = true;
    ++generation.current;
    setBusy(true);
    setError("");
    try {
      const result = (await reconcileCompanyOperation(pending)) as Checkout & { billingKind?: string };
      remember("");
      if (result.billingKind === "checkout" && result.state === "prepared") {
        setProposal(result);
        save(reviewKey, result.id);
        setConsent(false);
      }
      await load();
      setNotice("Your saved request was recovered.");
    } catch (e) {
      setError((e as Error).message);
    } finally {
      lock.current = false;
      setBusy(false);
    }
  }
  async function act(action: string, fields: Record<string, unknown> = {}) {
    if (lock.current || pending) return;
    lock.current = true;
    ++generation.current;
    setBusy(true);
    setError("");
    setNotice("");
    try {
      const result = (await command(action, fields, remember)) as Checkout;
      remember("");
      if (action === "billing.prepare") {
        setProposal(result);
        setConsent(false);
        save(reviewKey, result.id);
        window.setTimeout(() => {
          reviewPanel.current?.focus({ preventScroll: true });
          reviewPanel.current?.scrollIntoView({ block: "start" });
        }, 0);
      } else if (action === "billing.checkout") {
        setProposal(null);
        setConsent(false);
        save(reviewKey, "");
      }
      await load();
    } catch (e) {
      if (!(e instanceof UnconfirmedCompanyOperation)) remember("");
      setError((e as Error).message);
      await load().catch(() => {});
    } finally {
      lock.current = false;
      setBusy(false);
    }
  }
  useEffect(() => {
    if (!owner) return;
    let alive = true;
    void (async () => {
      await load();
      let saved = "",
        operation = "";
      try {
        saved = sessionStorage.getItem(reviewKey) || "";
        operation = sessionStorage.getItem(pendingKey) || "";
      } catch {
        /* Server state remains authoritative. */
      }
      if (!alive) return;
      setPending(operation);
      if (saved) {
        const review = await api<Checkout>("/company/billing/checkouts/" + encodeURIComponent(saved));
        if (alive && review.state === "prepared") setProposal(review);
        else save(reviewKey, "");
      }
      if (alive && new URLSearchParams(location.search).get("billingReturn") === "1") await checkPayment();
    })().catch((e) => {
      if (alive)
        setError(
          e instanceof ApiError && e.status === 0
            ? "Billing could not be loaded. Retry to check your saved subscription."
            : e.message,
        );
    });
    return () => {
      alive = false;
      ++generation.current;
    };
  }, [owner, scope, reload]);
  useEffect(() => {
    if (!owner || !data) return;
    const waiting =
      (!terminal.has(data.state.status) && data.state.status !== "active") ||
      ["queued", "dispatching"].includes(data.portal?.state || "");
    const timer = window.setInterval(
      () => {
        if (!lock.current) void load().catch((e) => setError(e.message));
      },
      waiting ? 2000 : 30_000,
    );
    return () => window.clearInterval(timer);
  }, [owner, data?.state.status, data?.portal?.state]);

  if (!owner)
    return (
      <section className="co-billing">
        <h2>Billing</h2>
        <p>Only a company owner can view subscription and payment details.</p>
      </section>
    );
  const active = data?.state.status === "active" && !!data.allowance;
  const status =
    data?.state.status === "active" && !active ? "payment-unverified" : data?.state.status || "not-configured";
  const copy = statusText[status] || [
    "Payment status unavailable",
    "Check the saved subscription status before starting more work.",
  ];
  const canChoose = data?.enabled && terminal.has(data.state.status);
  const link = data?.checkout?.receipt;
  const checkoutUrl = link?.status === "open" && new Date(link.expiresAt).getTime() > Date.now() ? link.url : null;
  const portalUrl =
    data?.portal?.state === "open" && new Date(data.portal.expiresAt).getTime() > Date.now()
      ? data.portal.receipt?.url
      : null;
  const expiredReview = proposal && new Date(proposal.expiresAt).getTime() <= Date.now();
  return (
    <section className="co-billing" aria-label="Company billing">
      <header className="co-billing-heading">
        <div>
          <p className="co-billing-eyebrow">Your subscription</p>
          <h2>Billing</h2>
          <p>Know what is included before your team gets to work.</p>
        </div>
        <button onClick={onUsage}>View usage</button>
      </header>
      {error && (
        <p className="co-error" role="alert">
          {error}
        </p>
      )}
      {notice && <p role="status">{notice}</p>}
      {pending && (
        <div className="co-billing-callout">
          <strong>Your last request needs confirmation</strong>
          <p>Check the saved result before submitting another request.</p>
          <button disabled={busy} onClick={() => void recover()}>
            Recover saved request
          </button>
        </div>
      )}
      {!data ? (
        error ? (
          <button
            onClick={() => {
              setError("");
              setReload((value) => value + 1);
            }}
          >
            Retry loading billing
          </button>
        ) : (
          <p role="status">Loading billing…</p>
        )
      ) : (
        <>
          {!data.configured ? (
            <article className="co-billing-card">
              <h3>{data.required ? "Billing temporarily unavailable" : "Billing is not set up yet"}</h3>
              <p>
                {data.required
                  ? "Your subscription records are saved. Ask your workspace operator to restore the billing connection before starting new paid work."
                  : "There are no paid plans to select in this workspace. Your existing usage safety limits still apply."}
              </p>
            </article>
          ) : (
            <>
              {!data.liveMode && (
                <p className="co-billing-mode">Test mode · These payment flows do not take real payments.</p>
              )}
              {!data.enabled && (
                <p className="co-billing-callout">
                  New subscriptions are currently unavailable. You can still review and manage your existing billing
                  account.
                </p>
              )}
              {!(proposal && data.state.status === "subscription-required") && (
                <article className={`co-billing-card co-billing-status ${active ? "is-active" : ""}`}>
                  <p className="co-billing-eyebrow">{data.checkout?.plan.name || "Subscription status"}</p>
                  <h3>{copy[0]}</h3>
                  <p>{copy[1]}</p>
                  {data.checkout && (
                    <p className="co-billing-price">
                      {billingPrice(data.checkout.plan.price)}
                      <span> / month</span>
                    </p>
                  )}
                  {data.state.cancelAtPeriodEnd && data.state.periodEnd && (
                    <p className="co-billing-callout">
                      Cancels at the end of the current period: {date(data.state.periodEnd)}.
                    </p>
                  )}
                  {active && data.allowance && (
                    <>
                      <h4>Included each UTC month</h4>
                      <Included allowance={data.allowance} />
                      <p>
                        Remaining: {number(Math.max(0, data.allowance.jobs - data.usage.jobs - data.usage.carriedJobs))}{" "}
                        AI jobs and{" "}
                        {number(
                          Math.max(
                            0,
                            data.allowance.tokens -
                              data.usage.tokens -
                              data.usage.heldTokens -
                              data.usage.carriedTokens,
                          ),
                        )}{" "}
                        tokens. Your safety limits can reduce this further.
                      </p>
                    </>
                  )}
                  <div className="co-billing-actions">
                    {checkoutUrl && (
                      <a className="co-billing-primary" href={checkoutUrl} target="_blank" rel="noopener noreferrer">
                        Continue to Stripe ↗
                      </a>
                    )}
                    {data.checkout && (
                      <button disabled={busy} aria-busy={checkingPayment} onClick={() => void checkPayment()}>
                        {checkingPayment ? "Checking payment…" : "Check payment status"}
                      </button>
                    )}
                    {portalUrl ? (
                      <a href={portalUrl} target="_blank" rel="noopener noreferrer">
                        Manage billing in Stripe ↗
                      </a>
                    ) : (
                      data.state.customerId && (
                        <button
                          disabled={busy || !!pending || ["queued", "dispatching"].includes(data.portal?.state || "")}
                          onClick={() =>
                            void act("billing.portal", {
                              expectedVersion: data.state.version,
                              decision: data.portal?.state === "uncertain" ? "create-new-link" : "open-billing",
                            })
                          }
                        >
                          {["queued", "dispatching"].includes(data.portal?.state || "")
                            ? "Preparing billing link…"
                            : data.portal?.state === "uncertain"
                              ? "Create a new billing link"
                              : "Manage billing"}
                        </button>
                      )
                    )}
                  </div>
                  {data.portal?.state === "uncertain" && (
                    <p>
                      The earlier portal link could not be confirmed. Creating a new link does not change your
                      subscription.
                    </p>
                  )}
                  {(checkoutUrl || portalUrl) && (
                    <p className="co-billing-note">Stripe opens in a new tab. Payment details stay with Stripe.</p>
                  )}
                  {data.state.verifiedAt && (
                    <p className="co-billing-note">Last verified {date(data.state.verifiedAt)}.</p>
                  )}
                </article>
              )}
              {proposal && canChoose && (
                <article
                  ref={reviewPanel}
                  tabIndex={-1}
                  className="co-billing-card co-billing-review"
                  aria-label="Review subscription"
                >
                  <p className="co-billing-eyebrow">Review before continuing</p>
                  <h3>{proposal.plan.name}</h3>
                  <p className="co-billing-price">
                    {billingPrice(proposal.plan.price)}
                    <span> / month</span>
                  </p>
                  <Included allowance={proposal.plan.allowance} />
                  <p>
                    This subscription renews monthly until canceled. Included usage resets at 00:00 UTC on the first of
                    each calendar month, separately from the payment renewal date. Unresolved work continues to hold its
                    allowance.
                  </p>
                  <p>
                    Review the final total and any applicable tax in Stripe before paying. Preparing checkout does not
                    activate your allowance.
                  </p>
                  <label className="co-billing-consent">
                    <input
                      type="checkbox"
                      checked={consent}
                      onChange={(e) => setConsent(e.target.checked)}
                      disabled={busy || !!expiredReview}
                    />
                    <span>
                      I approve {proposal.plan.name} at {billingPrice(proposal.plan.price)} per month and the listed
                      allowance.
                    </span>
                  </label>
                  {expiredReview && (
                    <p role="status">This review expired. Choose the plan again for a current review.</p>
                  )}
                  <div className="co-billing-actions">
                    <button
                      className="co-billing-primary"
                      disabled={busy || !!pending || !consent || !!expiredReview}
                      onClick={() =>
                        void act("billing.checkout", {
                          id: proposal.id,
                          expectedVersion: proposal.version,
                          digest: proposal.digest,
                          decision: "approve-subscription",
                        })
                      }
                    >
                      {busy ? "Saving approval…" : "Prepare secure checkout"}
                    </button>
                    <button
                      disabled={busy}
                      onClick={() => {
                        setProposal(null);
                        setConsent(false);
                        save(reviewKey, "");
                      }}
                    >
                      Back to plans
                    </button>
                  </div>
                </article>
              )}
              {canChoose && !proposal && (
                <div className="co-billing-plans">
                  {data.plans.map((plan) => (
                    <article className="co-billing-card" key={plan.id}>
                      <h3>{plan.name}</h3>
                      <p className="co-billing-price">
                        {billingPrice(plan.price)}
                        <span> / month</span>
                      </p>
                      <Included allowance={plan.allowance} />
                      <p>Allowances reset monthly at 00:00 UTC. Your team still needs approval for its work.</p>
                      <button
                        disabled={busy || !!pending}
                        onClick={() => void act("billing.prepare", { scope: plan.id })}
                      >
                        Review {plan.name}
                      </button>
                    </article>
                  ))}
                </div>
              )}
            </>
          )}
        </>
      )}
    </section>
  );
}
