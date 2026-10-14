import {createRoot} from 'react-dom/client';
import './secureContext';
import './styles/style.css';
import './styles/raven.css';
import './styles/business-theme.css';
import './styles/app.css';
import './styles/views.css';
import './styles/shell.css';
import './styles/workspace.css';
import {HireZeroWorkspace,applyTheme} from './shell/Workspace';
import {preference} from './shell/api';
import {api} from './shell/api';
import {CompanyWorkspace} from './company/CompanyWorkspace';

applyTheme(preference('theme','system'));
const root=createRoot(document.getElementById('root')!);
api<{companyEnabled?:boolean}>('/session').then(session=>root.render(session.companyEnabled?<CompanyWorkspace/>:<HireZeroWorkspace/>)).catch(()=>root.render(<HireZeroWorkspace/>));
