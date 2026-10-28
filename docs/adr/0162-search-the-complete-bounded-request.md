# ADR 0162: Search the complete bounded request

Status: accepted

## Context

Document and memory retrieval used only the first 24 distinct non-stopword terms of a task or search request.
Long instructions could consume that prefix before the actual subject appeared. Relevant approved material was
then omitted from both search results and the captured model context, despite an exact indexed topic match.

## Decision

Search all distinct terms in the already bounded request text. Keep the existing indexed keyword ranking,
company/department/employee audience checks, approval and freshness checks, and returned-context ceilings.
Documents still return at most three excerpts, at most two per document, with each excerpt at most 800 characters.
Memory still returns at most three eligible approved records. Model packet packing and admission remain unchanged.

Keep query metadata compact: requests over 24 terms show the first and last twelve terms with an ellipsis and a
notice that the full request was searched. New results identify their methods as `lexical-full-request-v2` and
`lexical-memory-full-request-v2`. Previously captured packets retain their saved method, evidence and contents;
they are not regenerated or replayed. No source index, database schema or execution policy changes.

## Consequences

A specific subject near the end of a detailed task can contribute relevant context. Searching more of the request
does not send more source records to the model or widen access. Keyword matching can still miss paraphrases or
rank generic overlaps poorly; this change is not semantic retrieval or a claim of general relevance quality.
