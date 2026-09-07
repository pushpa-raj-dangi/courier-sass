import { afterEach, describe, expect, it, vi } from "vitest";
import { act, render, screen } from "@testing-library/react";
import { WALKTHROUGH_STOPS } from "../content";

// See motionVariants.test.tsx for why useReducedMotion is mocked directly rather than
// via window.matchMedia: motion-dom's real implementation caches a module-level
// singleton on first read that Vite's dependency pre-bundling keeps alive across
// tests, so a matchMedia-only mock only ever reflects whichever test ran first.
const { useReducedMotionMock } = vi.hoisted(() => ({ useReducedMotionMock: vi.fn() }));

vi.mock("framer-motion", async () => {
  const actual = await vi.importActual<typeof import("framer-motion")>("framer-motion");
  return { ...actual, useReducedMotion: useReducedMotionMock };
});

const { ProductWalkthrough } = await import("./ProductWalkthrough");

describe("ProductWalkthrough", () => {
  afterEach(() => {
    useReducedMotionMock.mockReset();
    vi.useRealTimers();
  });

  it("shows every stop statically, all at once, when reduced motion is preferred", () => {
    useReducedMotionMock.mockReturnValue(true);
    render(<ProductWalkthrough />);
    for (const stop of WALKTHROUGH_STOPS) {
      expect(screen.getByText(stop.label)).toBeInTheDocument();
    }
    expect(screen.queryByTestId("walkthrough-frame")).not.toBeInTheDocument();
  });

  it("starts on the first stop when motion is allowed", () => {
    useReducedMotionMock.mockReturnValue(false);
    render(<ProductWalkthrough />);
    expect(screen.getByTestId("walkthrough-frame")).toHaveTextContent(WALKTHROUGH_STOPS[0].label);
  });

  it("advances to the next stop after the cycle interval", () => {
    useReducedMotionMock.mockReturnValue(false);
    vi.useFakeTimers();
    render(<ProductWalkthrough />);
    act(() => {
      vi.advanceTimersByTime(2800);
    });
    expect(screen.getByTestId("walkthrough-frame")).toHaveTextContent(WALKTHROUGH_STOPS[1].label);
  });

  it("wraps back to the first stop after cycling through all four", () => {
    useReducedMotionMock.mockReturnValue(false);
    vi.useFakeTimers();
    render(<ProductWalkthrough />);
    act(() => {
      vi.advanceTimersByTime(2800 * WALKTHROUGH_STOPS.length);
    });
    expect(screen.getByTestId("walkthrough-frame")).toHaveTextContent(WALKTHROUGH_STOPS[0].label);
  });
});
