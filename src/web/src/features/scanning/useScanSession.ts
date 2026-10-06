"use client";

import { useCallback, useEffect, useReducer, useRef } from "react";
import { INITIAL_SCAN_SESSION, scanSessionReducer } from "./scanSessionReducer";
import { scanErrorMessage } from "./scanErrors";
import type { ScanContext, ScanMode, ScanOutcome } from "./modes";

export const MODE_CHANGED_MESSAGE = "Mode changed — scan again";

// Runs at most one submission at a time. Each request snapshots the mode and context active
// when it was scanned, so a queued scan is written as the operation the operator chose even if
// they switch mode or manifest before it runs. Mode/context/onSettled are read through refs so a
// re-render never restarts or duplicates an in-flight write.
export function useScanSession(mode: ScanMode | undefined, context: ScanContext, onSettled?: (outcome: ScanOutcome) => void) {
  const [state, dispatch] = useReducer(scanSessionReducer, INITIAL_SCAN_SESSION);
  const modeRef = useRef(mode);
  const contextRef = useRef(context);
  const onSettledRef = useRef(onSettled);
  modeRef.current = mode;
  contextRef.current = context;
  onSettledRef.current = onSettled;

  // Keyed on the request object: each promotion is a new object, and log-only updates (a
  // displaced queued scan) keep the same in-flight reference, so this never double-submits.
  useEffect(() => {
    const request = state.inFlight;
    if (!request) return;
    const finish = (outcome: ScanOutcome) => {
      dispatch({
        type: "settled",
        request,
        ok: outcome.ok,
        text: outcome.ok ? outcome.summary : outcome.message,
        at: new Date().toISOString(),
      });
      onSettledRef.current?.(outcome);
    };
    const submit = request.mode.submit;
    if (!submit) {
      // Defensive: requests are only accepted for modes that can submit, but never leave a
      // request in flight forever — that would silently queue every later scan.
      finish({ ok: false, awb: request.awb, message: MODE_CHANGED_MESSAGE });
      return;
    }
    let pending: Promise<ScanOutcome>;
    try {
      pending = submit(request.awb, request.context);
    } catch (error) {
      pending = Promise.reject(error);
    }
    pending.then(finish, (error: unknown) => finish({ ok: false, awb: request.awb, message: scanErrorMessage(error) }));
  }, [state.inFlight]);

  const request = useCallback((awb: string) => {
    const active = modeRef.current;
    if (!active?.submit) return;
    const trimmed = awb.trim().toUpperCase();
    if (trimmed) dispatch({ type: "requested", request: { awb: trimmed, mode: active, context: contextRef.current }, at: new Date().toISOString() });
  }, []);

  return { state, request };
}
