import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { SignaturePad } from "./SignaturePad";

describe("SignaturePad", () => {
  beforeEach(() => {
    HTMLCanvasElement.prototype.getContext = vi.fn().mockReturnValue({
      beginPath: vi.fn(),
      moveTo: vi.fn(),
      lineTo: vi.fn(),
      stroke: vi.fn(),
      clearRect: vi.fn(),
    }) as unknown as typeof HTMLCanvasElement.prototype.getContext;
    HTMLCanvasElement.prototype.toBlob = vi.fn(function (callback: BlobCallback) {
      callback(new Blob(["x"], { type: "image/png" }));
    });
    vi.spyOn(HTMLCanvasElement.prototype, "getBoundingClientRect").mockReturnValue({ left: 0, top: 0 } as DOMRect);
  });

  it("calls onChange with a blob after drawing", () => {
    const onChange = vi.fn();
    render(<SignaturePad onChange={onChange} />);
    const canvas = screen.getByRole("img", { name: /signature pad/i });

    fireEvent.pointerDown(canvas, { clientX: 10, clientY: 10 });
    fireEvent.pointerMove(canvas, { clientX: 20, clientY: 20 });
    fireEvent.pointerUp(canvas);

    expect(onChange).toHaveBeenCalledWith(expect.any(Blob));
  });

  it("clear resets the canvas and calls onChange with null", () => {
    const onChange = vi.fn();
    render(<SignaturePad onChange={onChange} />);
    const canvas = screen.getByRole("img", { name: /signature pad/i });
    fireEvent.pointerDown(canvas, { clientX: 10, clientY: 10 });
    fireEvent.pointerMove(canvas, { clientX: 20, clientY: 20 });
    fireEvent.pointerUp(canvas);

    fireEvent.click(screen.getByRole("button", { name: /clear/i }));

    expect(onChange).toHaveBeenLastCalledWith(null);
  });
});
