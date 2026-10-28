import type { BusinessSource, ProfileField } from "./api";
import { BusinessFieldEvidence } from "./BusinessHistory";

export type TaskBusinessContextData = {
  companyKnowledge: string;
  businessBrief?: {
    profileId: string;
    digest: string;
    businessVersion: number;
    approvedAt: string;
    fields: ProfileField[];
    sources: Pick<BusinessSource, "id" | "url" | "title" | "digest" | "retrievedAt" | "clipped">[];
    unfilledFields?: string[];
    freshness?: { fields: { field: string; status: string; ownerNote?: string | null; checkedAt?: string | null }[] };
  };
  websiteResearch?: { website: string; notice: string; sources: BusinessSource[] };
};

const labels: Record<string, string> = {
  business: "Business",
  services: "Products and services",
  customers: "Customers",
  voice: "Voice",
  market: "Market",
  competitors: "Competitors",
  opportunities: "Opportunities",
  goals: "Goals",
  boundaries: "Boundaries",
};

function SavedKnowledge({ body }: { body: string }) {
  try {
    const fields = JSON.parse(body);
    if (fields && typeof fields === "object" && !Array.isArray(fields))
      return (
        <dl className="co-packet-fields">
          {Object.entries(fields).map(([key, value]) => (
            <div key={key}>
              <dt>{key.replace(/([A-Z])/g, " $1")}</dt>
              <dd>{typeof value === "string" ? value : JSON.stringify(value)}</dd>
            </div>
          ))}
        </dl>
      );
  } catch {
    /* Older plain-text context stays verbatim. */
  }
  return <pre>{body}</pre>;
}

export function TaskBusinessContext({ context }: { context: TaskBusinessContextData }) {
  const brief = context.businessBrief;
  const research = context.websiteResearch;
  return (
    <section className="co-task-business-context" aria-label="Business context supplied">
      <h4>Business context supplied</h4>
      <p className="co-muted">
        This is the saved context for this request. Later business changes are not reflected here.
      </p>
      {brief && (
        <>
          <p>
            Brief approved {new Date(brief.approvedAt).toLocaleString()} · business version {brief.businessVersion}
          </p>
          <dl className="co-packet-fields">
            {brief.fields.map((field) => {
              const freshness = brief.freshness?.fields.find((item) => item.field === field.key);
              return (
                <div key={field.key}>
                  <dt>{labels[field.key] ?? field.key}</dt>
                  <dd>
                    <p style={{ whiteSpace: "pre-wrap" }}>{field.value}</p>
                    <BusinessFieldEvidence field={field} sources={brief.sources} collapsible />
                    {freshness && (
                      <p className="co-muted">
                        At capture: {freshness.status.replaceAll("-", " ")}
                        {freshness.checkedAt && ` · checked ${new Date(freshness.checkedAt).toLocaleString()}`}
                        {freshness.ownerNote && ` · ${freshness.ownerNote}`}
                      </p>
                    )}
                  </dd>
                </div>
              );
            })}
          </dl>
          {!!brief.unfilledFields?.length && (
            <p className="co-muted">
              Not separately filled in: {brief.unfilledFields.map((key) => labels[key] ?? key).join(", ")}. Facts may
              still appear in the business description or other fields.
            </p>
          )}
          {!!brief.sources.length && (
            <p className="co-muted">
              Source-supported claims refer to saved excerpts, not independent verification today.
            </p>
          )}
        </>
      )}
      {context.companyKnowledge && (
        <details open={!brief}>
          <summary>{brief ? "Additional approved company context" : "Approved company context"}</summary>
          <SavedKnowledge body={context.companyKnowledge} />
        </details>
      )}
      {research && (
        <section aria-label="Website reference material">
          <h4>Website reference material</h4>
          <p>Public pages supplied for this task. They are not approved company knowledge.</p>
          <p>{research.notice}</p>
          {research.sources.map((source) => (
            <details key={source.id}>
              <summary>{source.title || source.url}</summary>
              <p>
                {source.url} · retrieved {new Date(source.retrievedAt).toLocaleString()}
                {source.clipped && " · Limited excerpt"}
              </p>
              <pre>{source.text}</pre>
            </details>
          ))}
          {!research.sources.length && <p>No page text was available in this request.</p>}
        </section>
      )}
      {!brief && !context.companyKnowledge && !research && <p>No business brief or website reference was included.</p>}
    </section>
  );
}
