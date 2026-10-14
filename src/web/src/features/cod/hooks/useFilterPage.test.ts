import { describe, expect, it } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { useFilterPage } from "./useFilterPage";

describe("useFilterPage", () => {
  it("keeps the chosen page while the filter is unchanged and resets to 1 when it changes", () => {
    const { result, rerender } = renderHook(({ filterKey }) => useFilterPage(filterKey), { initialProps: { filterKey: "a" } });
    expect(result.current[0]).toBe(1);

    act(() => result.current[1](3));
    expect(result.current[0]).toBe(3);

    rerender({ filterKey: "b" });
    expect(result.current[0]).toBe(1);

    act(() => result.current[1](2));
    expect(result.current[0]).toBe(2);
  });
});
