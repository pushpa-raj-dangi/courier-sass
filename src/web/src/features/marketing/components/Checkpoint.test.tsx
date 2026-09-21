import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

const { useReducedMotionMock } = vi.hoisted(() => ({ useReducedMotionMock: vi.fn() }));
vi.mock("framer-motion", async () => {
  const actual = await vi.importActual<typeof import("framer-motion")>("framer-motion");
  return { ...actual, useReducedMotion: useReducedMotionMock };
});

const { Checkpoint } = await import("./Checkpoint");

describe("Checkpoint", () => {
  it("shows a zero-padded numeral and is decorative", () => {
    useReducedMotionMock.mockReturnValue(false);
    render(<Checkpoint index={3} />);
    const node = screen.getByTestId("checkpoint");
    expect(node).toHaveTextContent("03");
    expect(node).toHaveAttribute("aria-hidden", "true");
  });
  it("is active (lit) by default under reduced motion", () => {
    useReducedMotionMock.mockReturnValue(true);
    render(<Checkpoint index={1} />);
    expect(screen.getByTestId("checkpoint")).toHaveAttribute("data-active", "true");
  });
});
