import { create } from "zustand";

interface AuthState {
  sessionExpired: boolean;
  showReauthDialog: () => void;
  hideReauthDialog: () => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  sessionExpired: false,
  showReauthDialog: () => set({ sessionExpired: true }),
  hideReauthDialog: () => set({ sessionExpired: false }),
}));
