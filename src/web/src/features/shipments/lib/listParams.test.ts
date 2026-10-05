import { describe, expect, it } from "vitest";
import { EMPTY_LIST_PARAMS, hasActiveFilters, parseListParams, toApiQuery, toSearchParams } from "./listParams";

describe("listParams", () => {
  it("parses defaults from an empty query string", () => {
    expect(parseListParams(new URLSearchParams())).toEqual(EMPTY_LIST_PARAMS);
  });

  it("round-trips every field through the URL", () => {
    const params = {
      ...EMPTY_LIST_PARAMS,
      tab: "ndr" as const,
      q: "sita",
      branchId: "b1",
      merchantId: "m1",
      serviceTypeId: "s1",
      booked: "custom" as const,
      from: "2026-10-01",
      to: "2026-10-05",
      page: 3,
    };
    expect(parseListParams(toSearchParams(params))).toEqual(params);
  });

  it("omits defaults from the URL", () => {
    expect(toSearchParams(EMPTY_LIST_PARAMS).toString()).toBe("");
  });

  it("ignores an unknown tab and a bad page", () => {
    const p = parseListParams(new URLSearchParams("tab=bogus&page=-4"));
    expect(p.tab).toBe("all");
    expect(p.page).toBe(1);
  });

  it("explicit status params override the tab", () => {
    const p = parseListParams(new URLSearchParams("tab=ndr&status=Created&status=PickupAssigned"));
    const api = toApiQuery(p, 20);
    expect(api.getAll("status")).toEqual(["Created", "PickupAssigned"]);
  });

  it("maps a tab to its statuses for the API", () => {
    const api = toApiQuery({ ...EMPTY_LIST_PARAMS, tab: "rto" }, 20);
    expect(api.getAll("status")).toEqual(["RtoInitiated", "RtoInTransit", "RtoAtOrigin", "RtoDelivered"]);
    expect(api.get("page")).toBe("1");
    expect(api.get("pageSize")).toBe("20");
  });

  it("sends q only when it has at least 2 non-space characters", () => {
    expect(toApiQuery({ ...EMPTY_LIST_PARAMS, q: " a " }, 20).has("q")).toBe(false);
    expect(toApiQuery({ ...EMPTY_LIST_PARAMS, q: " ab " }, 20).get("q")).toBe("ab");
  });

  it("turns booked presets and custom ranges into UTC-anchored bounds", () => {
    const now = new Date(2026, 9, 5, 9, 0);
    const today = toApiQuery({ ...EMPTY_LIST_PARAMS, booked: "today" }, 20, now);
    expect(today.get("createdFrom")).toBe("2026-10-05T00:00:00Z");
    expect(today.get("createdTo")).toBe("2026-10-05T23:59:59Z");
    const custom = toApiQuery({ ...EMPTY_LIST_PARAMS, booked: "custom", from: "2026-10-01", to: "" }, 20, now);
    expect(custom.get("createdFrom")).toBe("2026-10-01T00:00:00Z");
    expect(custom.has("createdTo")).toBe(false);
  });

  it("detects active filters but not the tab or page", () => {
    expect(hasActiveFilters({ ...EMPTY_LIST_PARAMS, tab: "ndr", page: 4 })).toBe(false);
    expect(hasActiveFilters({ ...EMPTY_LIST_PARAMS, merchantId: "m" })).toBe(true);
    expect(hasActiveFilters({ ...EMPTY_LIST_PARAMS, q: "x" })).toBe(true);
  });
});
