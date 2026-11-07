import type { Snapshot } from "./api";

// Customers of a hosted install (free sign-up companies) don't pay for the model, so what the install spends on AI is
// never shown to them in dollars; tokens used may be (docs/adr/0187). The workspace sets this from the company it shows
// before anything below it renders.
let customer = false;
export const setCustomerView = (value: boolean) => {
  customer = value;
};
export const customerView = () => customer;

// Tokens the company's model jobs have used, for the places that show dollars to everyone else.
export function tokensUsed(data: Snapshot["data"]): number {
  const jobs = [...(data.measured ?? []), ...(data.businessDrafts ?? [])];
  return jobs.reduce((sum, job) => sum + (job.usage ? job.usage.inputTokens + job.usage.outputTokens : 0), 0);
}

export const tokens = (count: number) => count.toLocaleString() + (count === 1 ? " token" : " tokens");
