import { afterEach, describe, expect, it, vi } from "vitest";
import { renderHook } from "@testing-library/react";

// framer-motion's real useReducedMotion lazily initializes a module-level singleton
// in motion-dom (hasReducedMotionListener) that reads window.matchMedia().matches
// exactly once per process and relies on a "change" event listener for updates
// afterward — Vite pre-bundles framer-motion/motion-dom for tests, so even
// vi.resetModules() + a fresh dynamic import can't force that singleton to
// re-initialize per test. Mocking useReducedMotion directly sidesteps that
// singleton entirely and makes each test's preference deterministic.
const { useReducedMotionMock } = vi.hoisted(() => ({ useReducedMotionMock: vi.fn() }));

vi.mock("framer-motion", async () => {
  const actual = await vi.importActual<typeof import("framer-motion")>("framer-motion");
  return { ...actual, useReducedMotion: useReducedMotionMock };
});

const { useRevealVariants } = await import("./motionVariants");

describe("useRevealVariants", () => {
  afterEach(() => {
    useReducedMotionMock.mockReset();
  });

  it("returns animated item variants when motion is allowed", () => {
    useReducedMotionMock.mockReturnValue(false);
    const { result } = renderHook(() => useRevealVariants());
    expect(result.current.prefersReducedMotion).toBe(false);
    expect(result.current.item.hidden).toEqual({ opacity: 0, y: 12 });
  });

  it("returns static, fully-visible item variants when prefers-reduced-motion is set", () => {
    useReducedMotionMock.mockReturnValue(true);
    const { result } = renderHook(() => useRevealVariants());
    expect(result.current.prefersReducedMotion).toBe(true);
    expect(result.current.item.hidden).toEqual({ opacity: 1, y: 0 });
    expect(result.current.item.visible).toEqual({ opacity: 1, y: 0 });
  });

  it("always returns a container with staggerChildren", () => {
    useReducedMotionMock.mockReturnValue(false);
    const { result } = renderHook(() => useRevealVariants());
    expect(result.current.container.visible).toEqual({ transition: { staggerChildren: 0.08 } });
  });
});
