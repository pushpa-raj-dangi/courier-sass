import { create } from "zustand";
import type { NotificationChannel } from "@/features/admin/settings/notification-templates/types";

type Panel = { mode: "closed" } | { mode: "edit"; eventType: string; channel: NotificationChannel };

interface NotificationTemplatePanelStore {
  panel: Panel;
  openEdit: (eventType: string, channel: NotificationChannel) => void;
  close: () => void;
}

// Global, same convention as ndrPanelStore/branchPanelStore — the edit Sheet opens over any row
// in the DataTable, list and detail live in separate components.
export const useNotificationTemplatePanelStore = create<NotificationTemplatePanelStore>((set) => ({
  panel: { mode: "closed" },
  openEdit: (eventType, channel) => set({ panel: { mode: "edit", eventType, channel } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
