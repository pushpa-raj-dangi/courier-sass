import { describe, expect, it, vi } from "vitest";
import { act, renderHook, waitFor } from "@testing-library/react";
import { useScanSession } from "./useScanSession";
import type { ScanMode, ScanOutcome } from "./modes";

function deferred() {
  let resolve!: (o: ScanOutcome) => void;
  const promise = new Promise<ScanOutcome>((r) => (resolve = r));
  return { promise, resolve };
}

describe("useScanSession", () => {
  it("serialises submissions: one in flight, then the queued one", async () => {
    const first = deferred();
    const second = deferred();
    const submit = vi.fn().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
    const mode = { id: "dispatch", submit } as unknown as ScanMode;
    const { result } = renderHook(() => useScanSession(mode, { kind: "none" }));

    act(() => result.current.request("A"));
    act(() => result.current.request("B"));
    expect(submit).toHaveBeenCalledTimes(1);

    await act(async () => first.resolve({ ok: true, awb: "A", summary: "ok" }));
    await waitFor(() => expect(submit).toHaveBeenCalledTimes(2));
    expect(submit.mock.calls[1][0]).toBe("B");

    await act(async () => second.resolve({ ok: false, awb: "B", message: "no" }));
    expect(result.current.state.log.map((e) => [e.awb, e.ok])).toEqual([["B", false], ["A", true]]);
  });

  it("ignores requests when the mode has no submit", () => {
    const { result } = renderHook(() => useScanSession({ id: "sort" } as ScanMode, { kind: "none" }));
    act(() => result.current.request("A"));
    expect(result.current.state.inFlight).toBeNull();
  });
});
