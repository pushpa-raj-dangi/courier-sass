import { create } from "zustand";

type OrganizationPanelState = { mode: "closed" } | { mode: "detail"; organizationId: string };

interface OrganizationPanelStore {
  panel: OrganizationPanelState;
  openDetail: (organizationId: string) => void;
  close: () => void;
}

// Global so any component in the platform tree — a list row, and later Task 18's
// impersonate button — can open the organization sheet without routing to a
// separate page (CLAUDE.md convention: detail/create UI is always a right-side
// Sheet, never its own route). No "create" mode: organizations are only ever
// created via self-registration, never by a SuperAdmin from this panel.
export const useOrganizationPanelStore = create<OrganizationPanelStore>((set) => ({
  panel: { mode: "closed" },
  openDetail: (organizationId) => set({ panel: { mode: "detail", organizationId } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
