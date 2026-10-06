"use client";

import { useCallback, useEffect, useReducer, useRef } from "react";
import { INITIAL_SCAN_SESSION, scanSessionReducer } from "./scanSessionReducer";
import type { ScanContext, ScanMode, ScanOutcome } from "./modes";

// Runs at most one submission at a time. Mode/context/onSettled are read through refs so a
// re-render never restarts or duplicates an in-flight write.
export function useScanSession(mode: ScanMode | undefined, context: ScanContext, onSettled?: (outcome: ScanOutcome) => void) {
  const [state, dispatch] = useReducer(scanSessionReducer, INITIAL_SCAN_SESSION);
  const modeRef = useRef(mode);
  const contextRef = useRef(context);
  const onSettledRef = useRef(onSettled);
  modeRef.current = mode;
  contextRef.current = context;
  onSettledRef.current = onSettled;

  useEffect(() => {
    const awb = state.inFlight;
    const active = modeRef.current;
    if (!awb || !active?.submit) return;
    void active.submit(awb, contextRef.current).then((outcome) => {
      dispatch({
        type: "settled",
        awb,
        modeId: active.id,
        ok: outcome.ok,
        text: outcome.ok ? outcome.summary : outcome.message,
        at: new Date().toISOString(),
      });
      onSettledRef.current?.(outcome);
    });
  }, [state.inFlight, state.nextId]);

  const request = useCallback((awb: string) => {
    if (!modeRef.current?.submit) return;
    const trimmed = awb.trim().toUpperCase();
    if (trimmed) dispatch({ type: "requested", awb: trimmed });
  }, []);

  return { state, request };
}
