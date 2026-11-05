export type ModelOption = { route: string; provider: string; allowanceUnit: "credits" | "usd-cents" };

export function providerName(provider: string) {
  return (
    (
      {
        "codex-oauth": "Codex subscription",
        vultr: "Vultr",
        openrouter: "OpenRouter API",
        openai: "OpenAI API",
        anthropic: "Anthropic API",
        "host-api": "Host API",
        host: "Host connection",
      } as Record<string, string>
    )[provider] ?? provider
  );
}

export function ModelChoice({
  options = [],
  value,
  onChange,
  disabled = false,
}: {
  options?: ModelOption[];
  value: string;
  onChange: (route: string) => void;
  disabled?: boolean;
}) {
  if (!options.length) return null;
  const unavailable = !!value && !options.some((option) => option.route === value);
  return (
    <label className="co-model-choice">
      Model and account
      <select value={value} disabled={disabled} onChange={(event) => onChange(event.target.value)}>
        {unavailable && (
          <option value={value} disabled>
            Unavailable selection · {value}
          </option>
        )}
        <option value="">Task default · exact model shown in review</option>
        {options.map((option) => (
          <option key={option.route} value={option.route}>
            {providerName(option.provider)} · {option.route}
          </option>
        ))}
      </select>
      {unavailable && (
        <small role="status">This connection changed. Choose an available model before preparing another review.</small>
      )}
      <small>
        The selected account is used only after you review and authorize this job. No automatic provider fallback.
      </small>
    </label>
  );
}
