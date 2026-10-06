export type ScanModeId = "receive" | "sort" | "dispatch" | "handover" | "rto";

export interface ScanLogEntry {
  id: number;
  at: string;
  awb: string;
  modeId: ScanModeId;
  ok: boolean;
  text: string;
}

export interface ScanSessionState {
  inFlight: string | null;
  queued: string | null;
  last: ScanLogEntry | null;
  log: ScanLogEntry[];
  nextId: number;
}

export type ScanSessionAction =
  | { type: "requested"; awb: string }
  | { type: "settled"; awb: string; modeId: ScanModeId; ok: boolean; text: string; at: string };

export const MAX_LOG = 200;

export const INITIAL_SCAN_SESSION: ScanSessionState = { inFlight: null, queued: null, last: null, log: [], nextId: 1 };

// Mode and context live in the page (URL / component state); this reducer only owns the
// submission queue and the session log (a refinement of spec §6.5 — keeps mode in one place).
export function scanSessionReducer(state: ScanSessionState, action: ScanSessionAction): ScanSessionState {
  switch (action.type) {
    case "requested":
      if (action.awb === state.inFlight || action.awb === state.queued) return state;
      if (state.inFlight === null) return { ...state, inFlight: action.awb };
      return { ...state, queued: action.awb };
    case "settled": {
      const entry: ScanLogEntry = { id: state.nextId, at: action.at, awb: action.awb, modeId: action.modeId, ok: action.ok, text: action.text };
      return {
        inFlight: state.queued,
        queued: null,
        last: entry,
        log: [entry, ...state.log].slice(0, MAX_LOG),
        nextId: state.nextId + 1,
      };
    }
  }
}
