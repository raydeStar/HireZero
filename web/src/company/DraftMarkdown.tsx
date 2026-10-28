import { useId, useState, type ReactNode } from "react";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { draftCopySections } from "./draftCopySections";

function CopyHeading({
  level: Heading,
  children,
  text,
}: {
  level: "h1" | "h2" | "h3" | "h4" | "h5" | "h6" | "p";
  children?: ReactNode;
  text?: string;
}) {
  const id = useId();
  const [status, setStatus] = useState("");
  async function copy() {
    if (!text) return;
    try {
      await navigator.clipboard.writeText(text);
      setStatus("Section copied.");
    } catch {
      setStatus("Clipboard unavailable. Select the section text or use Download draft.");
    }
  }
  if (!text) return <Heading>{children}</Heading>;
  return (
    <div className="co-copy-heading">
      <div className="co-copy-heading-row">
        <Heading id={`${id}-heading`}>{children}</Heading>
        <button
          type="button"
          className="co-ghost"
          aria-labelledby={`${id}-copy ${id}-heading`}
          title="Copy this section as Markdown, without its heading"
          onClick={() => void copy()}
        >
          <span id={`${id}-copy`}>Copy section</span>
        </button>
      </div>
      <span role="status">{status}</span>
    </div>
  );
}

const copyHeadings = Object.fromEntries(
  (["h1", "h2", "h3", "h4", "h5", "h6", "p"] as const).map((level) => [
    level,
    ({ node, children }: { node?: { properties: Record<string, unknown> }; children?: ReactNode }) => (
      <CopyHeading level={level} text={node?.properties["data-copy-text"] as string | undefined}>
        {children}
      </CopyHeading>
    ),
  ]),
);

// Render the saved draft faithfully; the butler may set the table, never rewrite its contents.
export function DraftMarkdown({ children, copySections = false }: { children: string; copySections?: boolean }) {
  return (
    <Markdown
      remarkPlugins={copySections ? [remarkGfm, draftCopySections] : [remarkGfm]}
      skipHtml
      disallowedElements={["img"]}
      components={{
        ...copyHeadings,
        a: ({ children, href }) => (
          <a href={href} target="_blank" rel="noopener noreferrer">
            {children}
          </a>
        ),
        table: ({ children }) => (
          <div className="co-draft-table" role="region" aria-label="Scrollable draft table" tabIndex={0}>
            <table>{children}</table>
          </div>
        ),
      }}
    >
      {children}
    </Markdown>
  );
}
