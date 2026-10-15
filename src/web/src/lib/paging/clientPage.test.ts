import { describe, expect, it } from "vitest";
import { toClientPage } from "./clientPage";

describe("toClientPage", () => {
  const items = Array.from({ length: 45 }, (_, i) => i + 1);

  it("slices one page and reports totals", () => {
    expect(toClientPage(items, 2, 20)).toEqual({ items: items.slice(20, 40), page: 2, pageSize: 20, totalCount: 45, totalPages: 3 });
  });

  it("clamps a page past the end to the last page", () => {
    expect(toClientPage(items, 9, 20).page).toBe(3);
    expect(toClientPage(items, 9, 20).items).toEqual([41, 42, 43, 44, 45]);
  });

  it("returns one empty page for an empty list", () => {
    expect(toClientPage([], 1, 20)).toEqual({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 1 });
  });
});
