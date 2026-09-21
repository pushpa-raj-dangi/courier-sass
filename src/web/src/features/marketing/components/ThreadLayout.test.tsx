import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

const { useReducedMotionMock } = vi.hoisted(() => ({ useReducedMotionMock: vi.fn() }));
vi.mock("framer-motion", async () => {
  const actual = await vi.importActual<typeof import("framer-motion")>("framer-motion");
  return { ...actual, useReducedMotion: useReducedMotionMock };
});

const { ThreadLayout } = await import("./ThreadLayout");

describe("ThreadLayout", () => {
  it("renders children and a decorative thread", () => {
    useReducedMotionMock.mockReturnValue(false);
    render(
      <ThreadLayout>
        <p>content</p>
      </ThreadLayout>
    );
    expect(screen.getByText("content")).toBeInTheDocument();
    expect(screen.getByTestId("thread")).toHaveAttribute("aria-hidden", "true");
  });
  it("draws the full progress rail statically under reduced motion", () => {
    useReducedMotionMock.mockReturnValue(true);
    render(<ThreadLayout>x</ThreadLayout>);
    // Framer omits an identity scaleY(1) as "none" — i.e. the rail is fully drawn, never scaled down.
    const transform = screen.getByTestId("thread-progress").style.transform;
    expect(transform === "none" || transform.includes("scaleY(1)")).toBe(true);
  });
});
