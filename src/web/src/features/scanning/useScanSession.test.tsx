import { describe, expect, it, vi } from "vitest";
import { act, renderHook, waitFor } from "@testing-library/react";
import { useScanSession } from "./useScanSession";
import type { ScanContext, ScanMode, ScanOutcome } from "./modes";
import type { Manifest } from "@/features/manifests/types";

function deferred() {
  let resolve!: (o: ScanOutcome) => void;
  let reject!: (e: unknown) => void;
  const promise = new Promise<ScanOutcome>((r, j) => {
    resolve = r;
    reject = j;
  });
  return { promise, resolve, reject };
}

const manifestCtx = (id: string): ScanContext => ({ kind: "manifest", manifest: { id, code: id } as Manifest });

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

  it("does not deadlock when switching to a mode without submit while a scan is queued", async () => {
    const first = deferred();
    const submit = vi.fn().mockReturnValueOnce(first.promise).mockResolvedValue({ ok: true, awb: "x", summary: "ok" });
    const dispatch = { id: "dispatch", submit } as unknown as ScanMode;
    const sort = { id: "sort" } as ScanMode;
    const { result, rerender } = renderHook(({ mode }) => useScanSession(mode, { kind: "none" }), { initialProps: { mode: dispatch } });

    act(() => result.current.request("A"));
    act(() => result.current.request("B"));
    rerender({ mode: sort });
    await act(async () => first.resolve({ ok: true, awb: "A", summary: "ok" }));
    await waitFor(() => expect(result.current.state.inFlight).toBeNull());
    expect(result.current.state.log.map((e) => e.awb)).toEqual(["B", "A"]);

    rerender({ mode: dispatch });
    act(() => result.current.request("C"));
    await waitFor(() => expect(result.current.state.log[0]?.awb).toBe("C"));
    expect(submit.mock.calls.map((c) => c[0])).toEqual(["A", "B", "C"]);
  });

  it("submits a queued scan with the mode and context it was scanned under", async () => {
    const first = deferred();
    const dispatchSubmit = vi.fn().mockReturnValueOnce(first.promise).mockResolvedValue({ ok: true, awb: "B", summary: "ok" });
    const receiveSubmit = vi.fn().mockResolvedValue({ ok: true, awb: "B", summary: "ok" });
    const dispatch = { id: "dispatch", submit: dispatchSubmit } as unknown as ScanMode;
    const receive = { id: "receive", submit: receiveSubmit } as unknown as ScanMode;
    const { result, rerender } = renderHook(({ mode, ctx }) => useScanSession(mode, ctx), {
      initialProps: { mode: dispatch, ctx: manifestCtx("X") },
    });

    act(() => result.current.request("A"));
    act(() => result.current.request("B"));
    rerender({ mode: receive, ctx: manifestCtx("Y") });
    await act(async () => first.resolve({ ok: true, awb: "A", summary: "ok" }));

    await waitFor(() => expect(dispatchSubmit).toHaveBeenCalledTimes(2));
    expect(dispatchSubmit.mock.calls[1]).toEqual(["B", manifestCtx("X")]);
    expect(receiveSubmit).not.toHaveBeenCalled();
    await waitFor(() => expect(result.current.state.log[0]).toMatchObject({ awb: "B", modeId: "dispatch" }));
  });

  it("settles a rejected submit as a failed scan and keeps going", async () => {
    const first = deferred();
    const submit = vi.fn().mockReturnValueOnce(first.promise).mockResolvedValue({ ok: true, awb: "B", summary: "ok" });
    const onSettled = vi.fn();
    const mode = { id: "dispatch", submit } as unknown as ScanMode;
    const { result } = renderHook(() => useScanSession(mode, { kind: "none" }, onSettled));

    act(() => result.current.request("A"));
    act(() => result.current.request("B"));
    await act(async () => first.reject(new TypeError("Failed to fetch")));

    await waitFor(() => expect(result.current.state.log.map((e) => [e.awb, e.ok])).toEqual([["B", true], ["A", false]]));
    expect(onSettled).toHaveBeenCalledWith(expect.objectContaining({ ok: false, awb: "A" }));
  });
});
