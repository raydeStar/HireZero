import {api,ApiError} from '../shell/api';
export type Employee={id:string;departmentId:string;roleId:string;name:string;responsibility:string;state:string;version:number};
export type Department={id:string;name:string;guide:string|null;knowledge:string|null;version:number};
export type Knowledge={id:string;departmentId:string|null;body:string;digest:string;state:string;version:number};
export type Thread={id:string;employeeId:string;title:string};
export type Work={id:string;employeeId:string;threadId:string;skill:string;input:string;state:string;reason:string|null;version:number;rootId:string};
export type Artifact={id:string;taskId:string;employeeId:string;body:string;digest:string;state:string;provenance:string;version:number};
export type TaskSpec={employeeId:string;threadId:string;skill:string;input:string;evidence:string[];dependsOn:number[]};
export type Plan={id:string;ceoId:string;objective:string;tasks:TaskSpec[];digest:string;state:string;version:number;budget:number};
export type Meeting={id:string;title:string;agenda:string;participants:string[];chairId:string;state:string;version:number;turns:{employeeId:string;body:string;taskId:string}[];activeTask:string|null;outcome:string|null;outcomeDigest:string|null};
export type Event={sequence:number;type:string;id:string;employeeId:string|null;text:string;at:string};
export type Role={id:string;name:string;skills:string[];onboardingFields:string[]};
export type Snapshot={capabilities:{modelExecution:boolean;providerStatus:string};data:{departments:Department[];employees:Employee[];knowledge:Knowledge[];threads:Thread[];tasks:Work[];artifacts:Artifact[];roles:Role[];plans:Plan[];meetings:Meeting[];cursor:number;budgets:{id:string;limit:number;used:number;reserved:number}[]}};
export type ThreadDetail={thread:Thread;messages:{id:string;author:string;body:string}[];notes:{body:string;version:number}};
function pending(){try{return sessionStorage.getItem('hirezero-company-operation');}catch{return null;}}
function remember(id:string|null){try{if(id)sessionStorage.setItem('hirezero-company-operation',id);else sessionStorage.removeItem('hirezero-company-operation');}catch{/* A restricted browser still gets the current request's reconciliation. */}}
// Keep one attempt's operation ID until its outcome is known. A lost HTTP reply is reconciled first.
export async function command(action:string,fields:Record<string,unknown>={}){
  const uncertain=pending();if(uncertain){try{await api<unknown>('/company/operations/'+uncertain);remember(null);}catch{throw new Error('A previous operation remains unconfirmed. Refresh and inspect the saved state before proceeding. Operation: '+uncertain);}throw new Error('The previous operation is confirmed. Refresh the saved state, then choose the next action.');}
  const requestId=crypto.randomUUID();
  try{return await api<unknown>('/company/commands','POST',{requestId,action,...fields});}
  catch(error){if(error instanceof ApiError&&error.status===0){remember(requestId);try{const result=await api<unknown>('/company/operations/'+requestId);remember(null);return result;}catch{throw new Error('The operation outcome is unconfirmed. Refresh the saved view before submitting again. Operation: '+requestId);}}throw error;}
}
