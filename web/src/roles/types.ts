import type { ReactNode } from "react";
import type { Detail, Session, WorkRequest } from "../shell/api";

// What a hired role contributes to the shell. Every slot is optional; a fired role contributes nothing.
export interface WorkTabContext {
  session: Session;
  requests: WorkRequest[];
  order: string[];
  onOpen: (id: string) => void;
  onRefresh: () => Promise<void>;
  onForbidden: () => void;
}
export interface RequestTabContext {
  detail: Detail;
  session: Session;
  onRefresh: () => Promise<void>;
  onForbidden: () => void;
}
export interface ConversationContext {
  requestId: string;
  messageCount: number;
  session: Session;
  onForbidden: () => void;
}
export interface SectionContext {
  session: Session;
  onForbidden: () => void;
}
export interface RoleTab<T> {
  key: string;
  label: string;
  ownerOnly?: boolean;
  render: (context: T) => ReactNode;
}
// Long-running role work outside the agent queue (e.g. a coding run), shown on the Cockpit's "Working on" card.
export interface RoleActivity {
  requestId: string;
  label: string;
}
export interface RoleUi {
  id: string;
  name: string;
  workTabs?: RoleTab<WorkTabContext>[];
  requestTabs?: RoleTab<RequestTabContext>[];
  conversationActions?: (context: ConversationContext) => ReactNode;
  homeActions?: (context: SectionContext) => ReactNode;
  cockpitSections?: (context: SectionContext) => ReactNode;
  settingsSections?: (context: SectionContext) => ReactNode;
  activity?: { path: string; describe: (item: unknown) => RoleActivity | null };
}
