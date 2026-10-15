import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderHook } from "@testing-library/react";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/ndrs",
  useSearchParams: () => new URLSearchParams(search),
}));

import { useSearchParamState } from "./useSearchParamState";

beforeEach(() => {
  replace.mockReset();
  search = "";
});

describe("useSearchParamState", () => {
  it("reads params and writes a merged patch without scrolling", () => {
    search = "status=Open&reason=WrongAddress";
    const { result } = renderHook(() => useSearchParamState());
    expect(result.current.params.get("status")).toBe("Open");

    result.current.update({ assigned: "me", reason: undefined });
    expect(replace).toHaveBeenCalledWith("/ndrs?status=Open&assigned=me", { scroll: false });
  });

  it("drops the query string entirely when nothing is left", () => {
    search = "status=Open";
    const { result } = renderHook(() => useSearchParamState());
    result.current.update({ status: undefined });
    expect(replace).toHaveBeenCalledWith("/ndrs", { scroll: false });
  });
});
