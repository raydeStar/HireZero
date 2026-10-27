// Stripe charge units differ from payout units, notably for ISK, UGX, HUF and TWD.
// https://docs.stripe.com/currencies#zero-decimal
const zeroDecimal = new Set([
  "bif",
  "clp",
  "djf",
  "gnf",
  "jpy",
  "kmf",
  "krw",
  "mga",
  "pyg",
  "rwf",
  "vnd",
  "vuv",
  "xaf",
  "xof",
  "xpf",
]);
export function billingPrice(price: { currency: string; unitAmount: number }, locale?: string) {
  const digits = zeroDecimal.has(price.currency) ? 0 : 2;
  return new Intl.NumberFormat(locale, {
    style: "currency",
    currency: price.currency.toUpperCase(),
    maximumFractionDigits: digits,
  }).format(price.unitAmount / 10 ** digits);
}
