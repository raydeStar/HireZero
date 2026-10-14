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

applyTheme(preference('theme','system'));
createRoot(document.getElementById('root')!).render(<HireZeroWorkspace/>);
