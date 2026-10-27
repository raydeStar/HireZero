# Dependency license notes

The HireZero source license and original copyright notice are in LICENSE and remain unchanged. This source repository references third-party packages through manifests and lockfiles; it does not vendor dependency installations or compiled runtime binaries.

Installed locked package metadata was reviewed at the publication checkpoint:

| Dependency | License |
| --- | --- |
| Microsoft.Data.Sqlite and Microsoft.Data.Sqlite.Core 10.0.3 | MIT |
| SQLite 3.53.4 | Public domain, as stated by its packaged LICENSE.txt |
| SQLitePCLRaw bundle/config/core/provider 3.0.5 | Apache 2.0 |
| React and React DOM 19.1.0 | MIT |
| react-markdown 10.1.0 | MIT |
| lucide-react 0.468.0 | ISC |
| TypeScript 5.8.3 | Apache 2.0 |
| Vite 6.4.3 | MIT |
| PdfPig 0.1.16 | Apache 2.0 |

Transitive packages retain their own package license notices. A future binary/distribution release must preserve the notices and any applicable attribution for everything it redistributes. These notes record the inspected primary package metadata; they do not replace the licenses or constitute an audit of every future build/distribution. Gitleaks and the local test browser are audit tools and are not included in the source release.

The optional browser image build candidate references the official Microsoft Playwright image by digest in
`runtime/browser/pin.json`. No base-image binaries are vendored here. Building or distributing that image includes
Ubuntu, Node, Chrome for Testing and other browser/system components with their own notices and obligations.
Preserve the upstream notices and complete a distribution review before publishing an image; the table above
does not constitute a license audit of that optional image. See ADR 0100 for its unqualified candidate status.
