import type { ScanContext, ScanMode } from "./modes";

export type ScanModeId = "receive" | "sort" | "dispatch" | "handover" | "rto";

export interface ScanLogEntry {
  id: number;
  at: string;
  awb: string;
  modeId: ScanModeId;
  ok: boolean;
  text: string;
}

// A scan as the operator made it: the code plus the mode and context active at scan time.
// A queued request is submitted with this snapshot, never with whatever mode/context is on
// screen when it is promoted (CLAUDE.md §39 — operator intent at scan time is the truth).
export interface ScanRequest {
  awb: string;
  mode: ScanMode;
  context: ScanContext;
}

export interface ScanSessionState {
  inFlight: ScanRequest | null;
  queued: ScanRequest | null;
  last: ScanLogEntry | null;
  log: ScanLogEntry[];
  nextId: number;
}

export type ScanSessionAction =
  | { type: "requested"; request: ScanRequest; at: string }
  | { type: "settled"; request: ScanRequest; ok: boolean; text: string; at: string };

export const MAX_LOG = 200;
export const SKIPPED_MESSAGE = "Skipped — scan again";

export const INITIAL_SCAN_SESSION: ScanSessionState = { inFlight: null, queued: null, last: null, log: [], nextId: 1 };

function contextKey(ctx: ScanContext): string {
  return ctx.kind === "manifest" ? `m:${ctx.manifest.id}` : ctx.kind === "rider" ? `r:${ctx.rider.id}` : "none";
}

function same(a: ScanRequest | null, b: ScanRequest): boolean {
  return a !== null && a.awb === b.awb && a.mode.id === b.mode.id && contextKey(a.context) === contextKey(b.context);
}

function append(state: ScanSessionState, request: ScanRequest, ok: boolean, text: string, at: string): Pick<ScanSessionState, "log" | "nextId"> & { entry: ScanLogEntry } {
  const entry: ScanLogEntry = { id: state.nextId, at, awb: request.awb, modeId: request.mode.id, ok, text };
  return { entry, log: [entry, ...state.log].slice(0, MAX_LOG), nextId: state.nextId + 1 };
}

// Mode and context live in the page (URL / component state); this reducer only owns the
// submission queue and the session log (a refinement of spec §6.5 — keeps mode in one place).
// The queue holds at most one request; a request displaced by a newer one is logged as
// skipped so every scan the operator made is visible.
export function scanSessionReducer(state: ScanSessionState, action: ScanSessionAction): ScanSessionState {
  switch (action.type) {
    case "requested": {
      if (same(state.inFlight, action.request) || same(state.queued, action.request)) return state;
      if (state.inFlight === null) return { ...state, inFlight: action.request };
      if (state.queued === null) return { ...state, queued: action.request };
      const { log, nextId } = append(state, state.queued, false, SKIPPED_MESSAGE, action.at);
      return { ...state, queued: action.request, log, nextId };
    }
    case "settled": {
      if (action.request !== state.inFlight) return state;
      const { entry, log, nextId } = append(state, action.request, action.ok, action.text, action.at);
      return { inFlight: state.queued, queued: null, last: entry, log, nextId };
    }
  }
}
