import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";

// Render the saved draft faithfully; the butler may set the table, never rewrite its contents.
export function DraftMarkdown({ children }: { children: string }) {
  return (
    <Markdown
      remarkPlugins={[remarkGfm]}
      skipHtml
      disallowedElements={["img"]}
      components={{
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
