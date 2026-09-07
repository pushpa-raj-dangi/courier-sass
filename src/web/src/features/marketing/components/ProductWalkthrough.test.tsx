import { afterEach, describe, expect, it, vi } from "vitest";
import { act, render, screen } from "@testing-library/react";
import { WALKTHROUGH_STOPS, SCREENSHOTS } from "../content";

const { useReducedMotionMock } = vi.hoisted(() => ({ useReducedMotionMock: vi.fn() }));

vi.mock("framer-motion", async () => {
  const actual = await vi.importActual<typeof import("framer-motion")>("framer-motion");
  return { ...actual, useReducedMotion: useReducedMotionMock };
});

const { ProductWalkthrough } = await import("./ProductWalkthrough");
const shipmentDetailScreenshot = SCREENSHOTS.find((s) => s.id === "shipment-detail")!;

describe("ProductWalkthrough", () => {
  afterEach(() => {
    useReducedMotionMock.mockReset();
    vi.useRealTimers();
  });

  it("shows every stop's label statically when reduced motion is preferred", () => {
    useReducedMotionMock.mockReturnValue(true);
    render(<ProductWalkthrough />);
    for (const stop of WALKTHROUGH_STOPS) {
      expect(screen.getByText(stop.label)).toBeInTheDocument();
    }
  });

  it("renders the real shipment-detail screenshot", () => {
    useReducedMotionMock.mockReturnValue(false);
    render(<ProductWalkthrough />);
    expect(screen.getByRole("img", { name: shipmentDetailScreenshot.alt })).toHaveAttribute(
      "src",
      shipmentDetailScreenshot.src
    );
  });

  it("starts the lifecycle rail on the first stop when motion is allowed", () => {
    useReducedMotionMock.mockReturnValue(false);
    render(<ProductWalkthrough />);
    expect(screen.getByTestId("walkthrough-rail")).toHaveTextContent(WALKTHROUGH_STOPS[0].label);
  });

  it("advances the rail to the next stop after the cycle interval", () => {
    useReducedMotionMock.mockReturnValue(false);
    vi.useFakeTimers();
    render(<ProductWalkthrough />);
    act(() => {
      vi.advanceTimersByTime(2800);
    });
    expect(screen.getByTestId("walkthrough-rail")).toHaveTextContent(WALKTHROUGH_STOPS[1].label);
  });

  it("wraps the rail back to the first stop after cycling through all four", () => {
    useReducedMotionMock.mockReturnValue(false);
    vi.useFakeTimers();
    render(<ProductWalkthrough />);
    act(() => {
      vi.advanceTimersByTime(2800 * WALKTHROUGH_STOPS.length);
    });
    expect(screen.getByTestId("walkthrough-rail")).toHaveTextContent(WALKTHROUGH_STOPS[0].label);
  });
});
