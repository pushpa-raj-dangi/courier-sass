import { create } from "zustand";
import type { AuditLogRecord } from "@/features/audit-logs/types";

type AuditLogPanelState = { mode: "closed" } | { mode: "detail"; record: AuditLogRecord };

interface AuditLogPanelStore {
  panel: AuditLogPanelState;
  openDetail: (record: AuditLogRecord) => void;
  close: () => void;
}

// Global so any row in the audit log table can open the detail Sheet without routing to a
// separate page (CLAUDE.md convention: detail views are always a right-side Sheet). The full
// record is already present in the list response the table already fetched — no extra request
// is made to show it.
export const useAuditLogPanelStore = create<AuditLogPanelStore>((set) => ({
  panel: { mode: "closed" },
  openDetail: (record) => set({ panel: { mode: "detail", record } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
