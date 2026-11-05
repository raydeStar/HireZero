# Hosted HireZero on Vultr · November 5, 2026

[ADR 0179](../adr/0179-hosted-hirezero-on-vultr.md) put the full app on the Agent Rush VM. This records how it was
deployed and its first real run.

## Deployment

- **Host:** the existing Vultr Cloud Compute VM (`vc2-2c-4gb`, Los Angeles), next to the engine demo, which stays at
  `rush.hirezero.app`. No resize: the app uses about 220 MB and the VM keeps more than 3 GB free.
- **Build:** `main` at the merge of the provider and sign-up work, built the way the Dockerfile does (web build, then
  a framework-dependent `dotnet publish`), run on the VM's pinned .NET 10 runtime.
- **Service:** its own system user, read-only system, write access only to a fresh data root, loopback port, memory
  cap. nginx terminates TLS for `app.hirezero.app` and overwrites the forwarded headers; the app trusts only that
  loopback proxy.
- **Settings:** a non-secret settings file with hosted mode, Google sign-in, the owner login,
  `HIREZERO_MODEL_VENDOR=vultr` and open sign-up with the default caps (50 cents per company, $5 per UTC day, 200
  companies).
- **Secrets:** the Vultr key, the sign-in client secret and a new key-encryption key are systemd credentials, never
  environment variables or settings. Stored token keys are encrypted.
- **Certificates:** Let's Encrypt standalone. The firewall keeps port 80 closed, so renewal hooks open it only for the
  challenge and close it again; a renewal dry run passed.
- **Sign-in:** a dedicated Google OAuth client (Web application) in production status, asking only for `openid`,
  `email` and `profile`.

## First run

The owner signed in, Home hired a Marketing specialist and prepared one job: three LinkedIn posts about trying
HireZero. The owner authorized it at up to $0.05.

- The first authorization was refused: "You are not a member of this company." Running an approved job acted as the
  configured owner login, but the first hosted sign-in re-keys that membership to the provider's subject. The run now
  acts as the signed-in member, with a regression test.
- The second authorization ran on `vultr/deepseek-v4.1-flash-1017` in 32 seconds: 2,594 input and 6,583 output tokens
  (5,520 reasoning), receipt `vultr:chatcmpl-…`. The spending gate reserved $0.0163 before sending and settled at
  $0.0043. The draft, about 5,000 characters, waits for owner review; nothing was posted.

## Not yet done

- Browser and script runtimes are not bound to the hosted app, so website reviews and scripts are unavailable there.
- The browser pane used for testing re-checks the session whenever it hides or shows, which briefly shows "Checking
  your session…"; ordinary browsers were not affected.
