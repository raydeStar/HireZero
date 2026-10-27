import { createRoot } from "react-dom/client";
import "./secureContext";
import "./styles/style.css";
import "./styles/raven.css";
import "./styles/business-theme.css";
import "./styles/app.css";
import "./styles/views.css";
import "./styles/shell.css";
import "./styles/workspace.css";
import { applyTheme, preference } from "./lib/api";
import { CompanyWorkspace } from "./company/CompanyWorkspace";
import { SignInGate } from "./company/SignIn";

applyTheme(preference("theme", "system"));
const root = createRoot(document.getElementById("root")!);
root.render(
  <SignInGate>
    {(session, onSignOut) => <CompanyWorkspace memberRole={session.role} onSignOut={onSignOut} />}
  </SignInGate>,
);
