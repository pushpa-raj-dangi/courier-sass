import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

const { useReducedMotionMock } = vi.hoisted(() => ({ useReducedMotionMock: vi.fn() }));
vi.mock("framer-motion", async () => {
  const actual = await vi.importActual<typeof import("framer-motion")>("framer-motion");
  return { ...actual, useReducedMotion: useReducedMotionMock };
});

const { CountUp } = await import("./CountUp");

describe("CountUp", () => {
  it("renders the final formatted value in the initial markup (no-JS / SSR safe)", () => {
    useReducedMotionMock.mockReturnValue(false);
    render(<CountUp value={2500} />);
    expect(screen.getByText("2,500")).toBeInTheDocument();
  });
  it("renders the final value immediately under reduced motion", () => {
    useReducedMotionMock.mockReturnValue(true);
    render(<CountUp value={2350} />);
    expect(screen.getByText("2,350")).toBeInTheDocument();
  });
});
