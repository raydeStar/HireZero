# 0054: Bounded business website discovery

Status: Accepted. Extends 0051 and 0052; uses the exact tool authority contract in 0007.

## Decision

Business lookup is a durable, company-scoped operation. The owner authorizes the exact saved identity and one
lookup. A company can start at most three searches and three discovery runs per hour, with one active lookup at
a time. Repeated operation IDs return their receipts. Restart fences interrupted runs as unknown; no worker
replays them. Completed, partial, withheld and unknown results are visible in the business brief.

Candidate search uses one request to the official Brave Web Search API. The server supplies only the business
name, location and industry, never the private goal or company knowledge. The `web.search` contract has Spend
risk. Its verifier consumes one exact query against a current owner-authorized record before dispatch; a second
invocation, another tenant, changed identity, altered query or revoked owner is refused. A trusted completion can
close a revoked initiator's request but withholds all returned data. API failures and lost responses are not retried.

The workspace operator supplies `HIREZERO_BRAVE_SEARCH_API_KEY` through the existing secret source, preferably a
mounted secret file. It is read at use, never sent to the browser or placed in company context. A missing key gives
a clear manual-website fallback. This is an operator-managed service credential; it is not customer BYOK or a claim
of tenant-scoped credential management. The operator must ensure its API plan permits the intended result storage,
display and traffic. Only request counts and dispatch audit are recorded; actual provider charges are not guessed.

Search returns at most five deduplicated HTTPS candidate sites. Titles and descriptions remain untrusted search
suggestions. Choosing a candidate only fills the identity form; the owner must save and confirm the business before
website import. Search itself never follows candidates, creates source evidence or approves knowledge.

Discovery reads the confirmed page and at most two relevant links from that page, prioritizing services/products,
about, pricing and blog pages. It does not recursively crawl child pages. Links stay on the confirmed origin;
credential-bearing URLs, query links, known account/action/download routes and explicit nofollow links are excluded.
Each read/redirect retains robots checks, pacing, public-address enforcement and byte/time bounds. The run has a
60-second deadline. Per-page failures preserve successful reads and explain the limitation. This is not a universal
terms-of-use checker or permission to read restricted social networks, customer accounts or competitor sites.

Source snapshots remain immutable and separate from approved knowledge. Discovery adds evidence, never silently
changes an approved profile. Existing manually chosen page reads remain available.

## Provider contract

Implemented against Brave's [Web Search reference](https://api-dashboard.search.brave.com/api-reference/web/search/post)
and [authentication guide](https://api-dashboard.search.brave.com/documentation/guides/authentication), inspected
2026-10-25: the fixed web-search endpoint, subscription-token header, bounded `count`, and `web.results` fields.
Routine checks use fake HTTP handlers exclusively; no paid account was configured or queried.
