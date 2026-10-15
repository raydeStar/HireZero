# Manual employee queue in HireZero

The company workspace now exposes the proven measured queue as an ordinary local UI flow. Automatic native model work remains off. Opening the app, assigning a brief, preparing its packet, refreshing, and restarting do not authorize a model request.

1. Open the local company workspace and choose **Open manual jobs**. An active employee whose role supports measured internal drafts is selected; otherwise hire and onboard an employee first.
2. Choose the draft work type, fill the campaign brief and assign it. The task says **Waiting for your one-job authorization**.
3. Enter an intended existing-credit allowance and choose **Prepare job for review**. No native request is sent. The allowance records intent; it cannot enforce a credit cutoff on an in-flight request.
4. Choose **Review task and context**. The review shows the requested work, approved department/company facts, notes, conversation history, selected evidence and employee instructions. IDs and a hidden digest are not a substitute for readable content. The server still binds approval to the exact saved version and packet.
5. Explicitly acknowledge the work and the lack of guaranteed upstream token, time and credit limits, then choose **Authorize and run one job**. This is spending authorization for only that job. Included usage must be available first; the 8,000-token reserve remains a planning estimate. No purchase, recharge or API fallback is provided.
6. Review the resulting internal artifact with **Approve content** or **Request changes**. Content approval does not authorize another call or publication. A completed or uncertain attempt offers no replay/resume control.

The pinned native connection uses the existing dedicated OAuth profile and verified process-local MXC restrictions. There are no new credentials or persistent permission/security changes. The strict provider qualification remains false; measured work is admitted only through the explicit one-job path. Developer execution and automatic queue dispatch stay unavailable.

## Start an existing company workspace

Build the frontend before the host, then use `tools/Start-CompanyLocal.ps1` with the existing company data root and its exact owner identity. The optional `-OwnerLogin` selects that existing owner without rewriting the database. Use `-Codex`, `-CodexExecutable`, `-CodexIdentity` and `-CodexHome` to select the existing pinned native connection. The launcher restores its process environment on exit, including removing variables that were previously absent.

The local URL is `http://127.0.0.1:5065/`. Company-local startup refuses a legacy database and a second scheduler for the same root. Existing data is preserved. Restart recovers dispatch fences and never replays a consumed native request. Before stopping an existing process, verify that it is the owned HireZero host for the selected workspace.

This URL grants local owner management authority and remains loopback-only. Forwarded and Tailscale identity headers are rejected. A separate read-only phone preview must keep its own static/demo assets and expiry; it must not be repointed at this live-control listener. Adding remote live controls would require a separately reviewed authenticated access design and owner approval.

## Verification of this integration

- Full solution build and TypeScript/Vite production build passed. The existing frontend bundle-size warning remains.
- Company checks passed 283; Host checks passed all 87 (44 held, 22 discovery, 21 recovery).
- Five Node/UI test files passed, including exact packet binding, explicit acknowledgement and consumed/unknown request controls.
- Fourteen company-launcher checks passed with a mocked process and no runtime/model call.
- Real installed Chrome exercised the manual UI against the test executable's unconditionally fake stdio provider: assignment and preparation caused zero dispatches; explicit acknowledgement caused exactly one fake native request; reload caused no replay; owner review and completion caused no additional call. Desktop/mobile rendering passed. Its fixture, browser profile and owned processes were disposable.

The test-only `--measured-browser-fixture` branch exists exclusively in the test executable and always selects the compiled fake provider. No production switch enables fake answers. Browser receipts and screenshots are ignored test output. The integration verification made no live model request; the earlier one-job live approval remains consumed.
