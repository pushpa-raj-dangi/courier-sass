import { create } from "zustand";

type AdminPanelState =
  | { mode: "closed" }
  | { mode: "role-create" }
  | { mode: "role-detail"; id: string }
  | { mode: "user-invite" }
  | { mode: "user-detail"; id: string };

interface AdminPanelStore {
  panel: AdminPanelState;
  openRoleCreate: () => void;
  openRoleDetail: (id: string) => void;
  openUserInvite: () => void;
  openUserDetail: (id: string) => void;
  close: () => void;
}

// One shared store for every /admin sheet (docs/ARCHITECTURE.md §9a), mirroring codPanelStore's
// discriminated-union-across-all-sheet-kinds shape — only one sheet is ever open at a time on
// this single-page, tabbed UI.
export const useAdminPanelStore = create<AdminPanelStore>((set) => ({
  panel: { mode: "closed" },
  openRoleCreate: () => set({ panel: { mode: "role-create" } }),
  openRoleDetail: (id) => set({ panel: { mode: "role-detail", id } }),
  openUserInvite: () => set({ panel: { mode: "user-invite" } }),
  openUserDetail: (id) => set({ panel: { mode: "user-detail", id } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
