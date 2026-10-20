// US dollars for spend (owner feedback, 2026-10-20): cents as usual, but an amount under one cent keeps two significant
// digits ($0.00064), so real but tiny model spend never reads as $0.00.
export function dollars(usd: number): string {
  const tiny = usd > 0 && usd < 0.01;
  return usd.toLocaleString(undefined, {
    style: "currency",
    currency: "USD",
    ...(tiny ? { maximumSignificantDigits: 2 } : { minimumFractionDigits: 2, maximumFractionDigits: 2 }),
  });
}
