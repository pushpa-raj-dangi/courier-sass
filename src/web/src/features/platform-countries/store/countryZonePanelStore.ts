import { create } from "zustand";

type CountryZonePanel = { mode: "closed" } | { mode: "create" } | { mode: "edit"; zoneId: string };

interface CountryZonePanelStore {
  panel: CountryZonePanel;
  openCreate: () => void;
  openEdit: (zoneId: string) => void;
  close: () => void;
}

// Global so the countries page's "New Zone" button and each zone row's edit action can open
// the same Sheet without routing to a separate page (CLAUDE.md convention: detail/create UI is
// always a right-side Sheet, never its own route). Country itself has no Sheet — its two mutable
// fields (isServiced, countryZoneId) are edited inline in the admin table row.
export const useCountryZonePanelStore = create<CountryZonePanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreate: () => set({ panel: { mode: "create" } }),
  openEdit: (zoneId) => set({ panel: { mode: "edit", zoneId } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
