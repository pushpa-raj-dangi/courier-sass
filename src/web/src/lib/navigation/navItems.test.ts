import { describe, expect, it } from "vitest";
import { NAV_ITEMS } from "./navItems";

describe("NAV_ITEMS", () => {
  it("has no separate live map entry; the map is a tab on /riders", () => {
    expect(NAV_ITEMS.some((item) => item.href === "/riders/live-map")).toBe(false);
    expect(NAV_ITEMS.some((item) => item.href === "/riders")).toBe(true);
  });
});
