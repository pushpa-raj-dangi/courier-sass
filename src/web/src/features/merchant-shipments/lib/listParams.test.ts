import { describe, expect, it } from "vitest";
import { MERCHANT_STATUS_GROUPS } from "./statusGroups";
import {
  EMPTY_MERCHANT_LIST_PARAMS,
  parseMerchantListParams,
  toMerchantApiQuery,
  toMerchantSearchParams,
} from "./listParams";

describe("merchant list params", () => {
  it("round-trips through the URL", () => {
    const p = { tab: "delivered" as const, q: "abc", from: "2026-11-01", to: "2026-11-10", serviceTypeId: "st1", page: 3 };
    expect(parseMerchantListParams(toMerchantSearchParams(p))).toEqual(p);
  });

  it("omits defaults from the URL", () => {
    expect(toMerchantSearchParams(EMPTY_MERCHANT_LIST_PARAMS).toString()).toBe("");
  });

  it("falls back for unknown tab and bad page", () => {
    const p = parseMerchantListParams(new URLSearchParams("tab=bogus&page=-2"));
    expect(p.tab).toBe("all");
    expect(p.page).toBe(1);
  });

  it("builds the API query with repeated statuses", () => {
    const api = toMerchantApiQuery({ ...EMPTY_MERCHANT_LIST_PARAMS, tab: "inTransit" }, 20);
    expect(api.getAll("status")).toEqual([...MERCHANT_STATUS_GROUPS.inTransit.statuses]);
    expect(api.getAll("status")).toHaveLength(11);
    expect(api.get("pageSize")).toBe("20");
    expect(api.get("page")).toBe("1");
  });

  it("sends UTC day bounds, q and service type", () => {
    const api = toMerchantApiQuery(
      { ...EMPTY_MERCHANT_LIST_PARAMS, q: "ab", from: "2026-11-01", to: "2026-11-02", serviceTypeId: "st1" },
      1
    );
    expect(api.get("createdFrom")).toMatch(/Z$/);
    expect(api.get("createdTo")).toMatch(/Z$/);
    expect(api.get("q")).toBe("ab");
    expect(api.get("serviceTypeId")).toBe("st1");
    expect(api.getAll("status")).toEqual([]);
  });
});
