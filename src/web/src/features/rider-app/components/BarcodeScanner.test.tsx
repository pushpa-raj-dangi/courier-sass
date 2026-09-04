import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { BarcodeScanner } from "./BarcodeScanner";

describe("BarcodeScanner", () => {
  beforeEach(() => {
    Object.defineProperty(navigator, "mediaDevices", {
      configurable: true,
      value: { getUserMedia: vi.fn().mockResolvedValue({ getTracks: () => [{ stop: vi.fn() }] }) },
    });
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
});
