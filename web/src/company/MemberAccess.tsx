import { createContext, useContext, type ReactNode } from "react";

const MemberRole = createContext<string | undefined>(undefined);

export function MemberAccess({ role, children }: { role?: string; children: ReactNode }) {
  return <MemberRole.Provider value={role}>{children}</MemberRole.Provider>;
}

// These flags shape the interface. Core still checks each command; a hidden key is not a lock.
export function useMemberAccess() {
  const role = useContext(MemberRole);
  const canManage = !role || role === "owner";
  return { canManage, canWork: canManage || role === "approver" };
}
