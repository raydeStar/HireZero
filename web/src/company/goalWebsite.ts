// Supply the boring prefix, not a different destination. The owner still confirms the exact website.
export function goalWebsite(value: string): { url: string | null; error: string } {
  const input = value.trim();
  if (!input) return { url: null, error: "" };
  try {
    const address = /^[a-z][a-z\d+.-]*:/i.test(input)
      ? input
      : input.startsWith("//")
        ? "https:" + input
        : "https://" + input;
    const parsed = new URL(address);
    if (parsed.protocol !== "https:") return { url: null, error: "Use a secure website address (https://)." };
    if (parsed.username || parsed.password)
      return { url: null, error: "Use a public website address without a username or password." };
    if (parsed.href.length > 800) return { url: null, error: "Use a shorter website address (up to 800 characters)." };
    return { url: parsed.href, error: "" };
  } catch {
    return { url: null, error: "Enter a website address, such as example.com or https://example.com." };
  }
}
