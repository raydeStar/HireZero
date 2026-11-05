# Local Codex xhigh in the company workspace

Accepted November 4, 2026.

The owner requested local testing through the existing HireZero company UI using ChatGPT OAuth and Luna xhigh. The company workspace already supports native Codex sign-in and the Luna route.

`HIREZERO_COMPANY_CODEX_XHIGH=1` selects an xhigh variant of each newly reviewed Codex text policy. Each variant retains its original byte, time, output and planning limits, adds `-xhigh` to its policy version and includes the effort in the approved digest. Existing Home and employee reviews display this value. The native connector confirms the effort before sending a turn.

The setting applies only to Codex OAuth routes. Earlier schedule approvals retain their exact policy. A pending unsent one-job review becomes stale if its configured policy changes. API model accounting follows its existing provider policy.

OAuth usage is measured after execution. Local output and time targets remain soft limits. The pinned runtime and dedicated sign-in profile supply authentication through the existing integration.
