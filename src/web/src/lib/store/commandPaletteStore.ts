import { create } from "zustand";

interface CommandPaletteStore {
  open: boolean;
  setOpen: (open: boolean) => void;
  toggle: () => void;
}

// Global so the ⌘K/Ctrl+K hotkey (registered once in CommandPalette) and the header's search
// button both drive the same dialog instance.
export const useCommandPaletteStore = create<CommandPaletteStore>((set) => ({
  open: false,
  setOpen: (open) => set({ open }),
  toggle: () => set((s) => ({ open: !s.open })),
}));
