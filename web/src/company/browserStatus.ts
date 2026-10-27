const messages: Record<string, string> = {
  "not-configured": "Website reviews need an isolated browser runtime. Ask the operator to configure one.",
  "operator-disabled": "The operator has paused new website reviews. Saved captures remain available.",
  checking: "Checking whether the isolated browser is ready. Refresh its status shortly.",
  "qualification-required": "The browser runtime needs operator verification before new website reviews can run.",
  "qualification-expired": "The browser runtime verification has expired. Ask the operator to renew it.",
  "engine-unavailable": "The isolated browser service cannot be reached. Ask the operator to restore it.",
  "image-unavailable": "The verified browser worker is unavailable. Ask the operator to restore it.",
  ready: "Website reviews are available. Each itinerary needs your exact approval.",
  fixture: "Fictional website-review runtime: no real websites are visited.",
};

export function browserRuntimeMessage(status: string) {
  return messages[status] || "New website reviews are unavailable. Saved captures remain available.";
}
