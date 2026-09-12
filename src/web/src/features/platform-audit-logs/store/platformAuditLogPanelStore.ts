import { create } from "zustand";
import type { AuditLogRecord } from "@/features/platform-audit-logs/types";

type PlatformAuditLogPanelState = { mode: "closed" } | { mode: "detail"; record: AuditLogRecord };

interface PlatformAuditLogPanelStore {
  panel: PlatformAuditLogPanelState;
  openDetail: (record: AuditLogRecord) => void;
  close: () => void;
}

export const usePlatformAuditLogPanelStore = create<PlatformAuditLogPanelStore>((set) => ({
  panel: { mode: "closed" },
  openDetail: (record) => set({ panel: { mode: "detail", record } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
