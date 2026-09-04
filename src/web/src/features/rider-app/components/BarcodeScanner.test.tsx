import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { BarcodeScanner } from "./BarcodeScanner";

// `new window.BarcodeDetector()` requires a real constructor — an arrow-function mock
// implementation throws "is not a constructor" the moment the component calls `new` on it
// (Vitest still records the call, so tests that only assert `toHaveBeenCalled()` don't notice,
// but tests that need `detect()` to actually run need this).
function stubBarcodeDetector(detect: ReturnType<typeof vi.fn>) {
  vi.stubGlobal(
    "BarcodeDetector",
    vi.fn().mockImplementation(function (this: { detect: typeof detect }) {
      this.detect = detect;
    })
  );
}

describe("BarcodeScanner", () => {
  beforeEach(() => {
    Object.defineProperty(navigator, "mediaDevices", {
      configurable: true,
      value: { getUserMedia: vi.fn().mockResolvedValue({ getTracks: () => [{ stop: vi.fn() }] }) },
    });
    // jsdom has no requestAnimationFrame implementation at all — without this, the component's
    // `rafId = requestAnimationFrame(loop)` call throws synchronously inside BarcodeScanner's own
    // try/catch, which is swallowed as a "camera unavailable" error and silently prevents the
    // detection loop from ever running. Stubbing it with a macrotask lets tests that need the loop
    // to actually invoke detect() (below) observe real detections.
    vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => setTimeout(() => cb(0), 0) as unknown as number);
    vi.stubGlobal("cancelAnimationFrame", (id: number) => clearTimeout(id));
  });
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("calls onDetect with the trimmed, uppercased value from manual entry, without ever requesting the camera", () => {
    const onDetect = vi.fn();
    render(<BarcodeScanner onDetect={onDetect} />);

    fireEvent.change(screen.getByLabelText(/enter awb manually/i), { target: { value: " awb001 " } });
    fireEvent.click(screen.getByRole("button", { name: /use this awb/i }));

    expect(onDetect).toHaveBeenCalledWith("AWB001");
    expect(navigator.mediaDevices.getUserMedia).not.toHaveBeenCalled();
  });

  it("does not request the camera on mount — only after the user taps Start Scanning", async () => {
    const detect = vi.fn().mockResolvedValue([{ rawValue: "AWB002" }]);
    vi.stubGlobal("BarcodeDetector", vi.fn().mockImplementation(() => ({ detect })));
    const onDetect = vi.fn();

    render(<BarcodeScanner onDetect={onDetect} />);
    expect(navigator.mediaDevices.getUserMedia).not.toHaveBeenCalled();
    expect(window.BarcodeDetector).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: /start scanning/i }));

    // The component's detection loop is driven by requestAnimationFrame in real use; this only
    // verifies BarcodeDetector was constructed once scanning starts, not the full camera loop
    // (covered by the manual E2E pass in Task 14 — jsdom has no real camera/video pipeline).
    await waitFor(() => expect(window.BarcodeDetector).toHaveBeenCalled());
  });

  it("stops the camera and shows Start Scanning again after a detection, instead of continuing to fire on the same barcode", async () => {
    const detect = vi.fn().mockResolvedValue([{ rawValue: "AWB003" }]);
    stubBarcodeDetector(detect);
    const onDetect = vi.fn();

    render(<BarcodeScanner onDetect={onDetect} />);
    fireEvent.click(screen.getByRole("button", { name: /start scanning/i }));

    await waitFor(() => expect(onDetect).toHaveBeenCalledWith("AWB003"));
    expect(onDetect).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(screen.getByRole("button", { name: /start scanning/i })).toBeInTheDocument());
  });

  it("does not re-request the camera when the parent re-renders with a new onDetect reference mid-scan", async () => {
    // Never resolves a barcode, so the scanning effect stays mounted (scanning stays true) —
    // this isolates the regression: a new onDetect identity alone must not restart getUserMedia.
    const detect = vi.fn().mockResolvedValue([]);
    stubBarcodeDetector(detect);

    const { rerender } = render(<BarcodeScanner onDetect={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: /start scanning/i }));

    await waitFor(() => expect(navigator.mediaDevices.getUserMedia).toHaveBeenCalledTimes(1));

    rerender(<BarcodeScanner onDetect={vi.fn()} />);

    expect(navigator.mediaDevices.getUserMedia).toHaveBeenCalledTimes(1);
  });
});
