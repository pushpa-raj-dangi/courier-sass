import { create } from "zustand";

/**
 * Shipments whose pickup, delivery or failed attempt was queued on this phone in this session.
 * Covers the gap after a sync empties the outbox but before `me/today` shows the task done, so the
 * rider can never act on the same stop twice. Pages forget an id once the run shows it not Pending.
 */
interface HandledState {
  handled: Set<string>;
  markHandled: (shipmentId: string) => void;
  forget: (shipmentId: string) => void;
}

export const useHandledStore = create<HandledState>((set) => ({
  handled: new Set(),
  markHandled: (shipmentId) =>
    set((s) => (s.handled.has(shipmentId) ? s : { handled: new Set(s.handled).add(shipmentId) })),
  forget: (shipmentId) =>
    set((s) => {
      if (!s.handled.has(shipmentId)) return s;
      const handled = new Set(s.handled);
      handled.delete(shipmentId);
      return { handled };
    }),
}));
