import { useEffect, useState } from "react";
import { customerView } from "./audience";

// What a job may spend, in the owner's terms (docs/adr/0177-plain-spend-limits.md). Included usage, such as a ChatGPT
// sign-in, has no per-job limit anyone can enforce, so its planning default is sent as it always was and never asked
// for. A paid API model keeps a real cap: entered and shown in dollars, sent and stored in US cents, and checked
// against the most the job could cost before anything is sent.
export const defaultAllowance = (cents: boolean) => (cents ? "5" : "1");

export const dollars = (cents: number) => "$" + (cents / 100).toFixed(2);

// The limit as a phrase: "up to $0.05", or "included usage". A customer of a hosted install sees no dollars: their
// jobs keep the default limit, checked on the server, and read as included in their plan (docs/adr/0187).
export const spendText = (cents: boolean, amount: number) =>
  customerView() ? "included in your plan" : cents ? "up to " + dollars(amount) : "included usage";

const toDollars = (cents: string) =>
  cents.trim() === "" || isNaN(Number(cents)) ? "" : (Number(cents) / 100).toFixed(2);
const fromDollars = (text: string) =>
  text.trim() === "" || isNaN(Number(text)) ? "" : String(Math.round(Number(text) * 10000) / 100);

// The spend limit for a paid model, typed in dollars and reported in cents; nothing at all for included usage.
export function SpendLimit({
  cents,
  value,
  onChange,
  disabled,
  label = "Spend limit",
}: {
  cents: boolean;
  value: string;
  onChange: (cents: string) => void;
  disabled?: boolean;
  label?: string;
}) {
  const [text, setText] = useState(() => toDollars(value));
  // A reset from outside (another model, a sent message) shows; the owner's own typing is left as typed.
  useEffect(() => {
    setText((current) => (fromDollars(current) === value ? current : toDollars(value)));
  }, [value]);
  if (!cents || customerView()) return null;
  return (
    <label className="co-spend-limit">
      {label}
      <span className="co-dollars">
        <span aria-hidden="true">$</span>
        <input
          type="number"
          min="0.01"
          max="10"
          step="0.01"
          inputMode="decimal"
          required
          disabled={disabled}
          aria-label={label + " in dollars"}
          title="Nothing is sent if the most this could cost is more than this."
          value={text}
          onChange={(e) => {
            setText(e.target.value);
            onChange(fromDollars(e.target.value));
          }}
        />
      </span>
    </label>
  );
}

// A free company's plan, in words: it is only a customer of this install, so it never sees what the install spends in
// dollars (docs/adr/0187). It speaks up when the plan can't run more work; spend already held for unfinished work is
// excluded.
export function FreeAllowance({
  remainingCents,
  companyRemainingCents,
}: {
  remainingCents: number;
  companyRemainingCents: number;
}) {
  if (remainingCents > 0) return null;
  return (
    <p className="co-muted" role="status">
      {companyRemainingCents > 0
        ? "Today's free usage is used up for everyone. It resets at midnight UTC."
        : "Your free plan is used up, or held for work that's still finishing."}
    </p>
  );
}
