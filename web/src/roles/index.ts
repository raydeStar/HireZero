import type {Session} from '../shell/api';
import type {RoleUi} from './types';
import {developer} from './developer';

// Every role UI this build knows about; the session's hired roles decide which are shown.
const available:RoleUi[]=[developer];
export const hiredRoles=(session:Session|null)=>available.filter(role=>session?.roles?.includes(role.id));
