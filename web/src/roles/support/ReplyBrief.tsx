// The brief for one customer reply (docs/adr/0034-support-employee.md). The message is redacted on the server before it
// is saved; the outcome is the only thing the draft may promise.
export const replyFields = ["message", "context", "outcome"] as const;

const labels: Record<(typeof replyFields)[number], string> = {
  message: "Customer message",
  context: "What you know (order, account, history)",
  outcome: "Outcome you want (the only thing the reply may promise)",
};

export function ReplyBrief({
  value,
  onChange,
}: {
  value: Record<string, string>;
  onChange: (value: Record<string, string>) => void;
}) {
  return (
    <fieldset>
      <legend>Customer reply</legend>
      {replyFields.map((field) => (
        <label key={field}>
          {labels[field]}
          {field === "message" ? (
            <textarea
              required
              rows={5}
              value={value[field] || ""}
              onChange={(e) => onChange({ ...value, [field]: e.target.value })}
            />
          ) : (
            <input
              required
              value={value[field] || ""}
              onChange={(e) => onChange({ ...value, [field]: e.target.value })}
            />
          )}
        </label>
      ))}
      <small>Emails, phone numbers and card numbers in the message are redacted before anything is drafted.</small>
    </fieldset>
  );
}
