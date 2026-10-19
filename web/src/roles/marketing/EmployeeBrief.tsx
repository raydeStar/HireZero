export const marketingFields = ["objective", "audience", "channel", "deliverable", "constraints"] as const;
export function EmployeeBrief({
  value,
  onChange,
}: {
  value: Record<string, string>;
  onChange: (value: Record<string, string>) => void;
}) {
  return (
    <fieldset>
      <legend>Campaign brief</legend>
      {marketingFields.map((field) => (
        <label key={field}>
          {field}
          <input
            required
            value={value[field] || ""}
            onChange={(e) => onChange({ ...value, [field]: e.target.value })}
          />
        </label>
      ))}
    </fieldset>
  );
}
