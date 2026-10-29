# Easier website entry on Home

Home now accepts a pasted website such as `paper.example.invalid` or
`www.paper.example.invalid/about` without requiring the owner to type `https://`. It trims surrounding
whitespace and shows the resolved HTTPS address in the discovery disclosure before consent. The
captured request uses that same address. Editing the input still clears consent.

Explicit insecure or unsupported schemes, credentials and malformed or overlong addresses remain
invalid. The server's public-source, identity, permission and bounded-discovery checks are unchanged.
This is input convenience, not permission to import website claims into approved company knowledge.
The resolved address wraps on narrow screens.

The audit confirmed that the ordinary lookup command already waits for discovery. No automatic
polling, model retry or approval change was needed. Uncertain replies still require checking the saved
request; website excerpts remain unverified context for that request.

## Verification

- Website normalization and goal-flow Node checks pass; the production web build passes with the
  existing bundle-size warning. CSharpier, web formatting and whitespace checks pass.
- The isolated Home browser check passes on desktop and mobile. It covers bare-address entry,
  explicit HTTP rejection, the displayed resolved address, refused setup, a lost save response and
  reload, exact single-run recovery, and a normal website-backed request completing from one consent.
- The browser fixture still expected duplicated brief text and explicit unknown fields. It now checks
  the current structured brief and missing-fields list, while separately asserting that discovered
  website content has not turned unknown services into approved facts. Failure snapshots are retained
  for diagnosis, and the new-result assertion waits for its own task rather than an earlier Home preview.
- Both installed web directories were backed up; all 16 new build files matched their installed copies
  and both hosts remained healthy. No services were restarted or application data changed by installation.

No live model or external website requests were made. The six remaining authorized Luna runs and the
pending owner assessment of the unedited photography-studio draft are unchanged. Synthetic success
does not establish real-website quality or customer onboarding acceptance.
