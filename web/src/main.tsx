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
import { JoinCompany } from "./company/JoinCompany";

applyTheme(preference("theme", "system"));
const root = createRoot(document.getElementById("root")!);
root.render(
  location.pathname.replace(/\/+$/, "").toLowerCase() === "/join" ? (
    <JoinCompany />
  ) : (
    <SignInGate>
      {(session, onSignOut) => (
        <CompanyWorkspace memberRole={session.role} onSignOut={onSignOut} canSignup={session.openSignup} />
      )}
    </SignInGate>
  ),
);
