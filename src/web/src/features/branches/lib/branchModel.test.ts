import { describe, expect, it } from "vitest";
import type { Branch } from "@/features/branches/types";
import {
  BRANCH_TYPES, branchTypeLabel, filterBranches, matchesBranchQuery, onHandShipmentsHref, outForDeliveryHref,
  parseBranchType, ridersOutHref, zeroActivity,
} from "./branchModel";

function branch(overrides: Partial<Branch>): Branch {
  return {
    id: "b1", code: "KTM-01", name: "Kathmandu Hub", type: "Hub", provinceId: "p1", districtId: "d1", localLevelId: "l1",
    wardNumber: 1, addressLine: "New Road", phone: null, operatingHours: null, latitude: null, longitude: null,
    parentBranchId: null, deliveryZoneId: null, isActive: true, ...overrides,
  };
}

describe("branchModel", () => {
  it("lists the five branch types in strip order with labels", () => {
    expect(BRANCH_TYPES.map((t) => t.type)).toEqual(["HeadOffice", "Hub", "Branch", "PickupPoint", "Warehouse"]);
    expect(branchTypeLabel("PickupPoint")).toBe("Pickup point");
    expect(parseBranchType("Warehouse")).toBe("Warehouse");
    expect(parseBranchType("warehouse")).toBeUndefined();
  });

  it("matches code or name case-insensitively and ignores blank queries", () => {
    const b = branch({});
    expect(matchesBranchQuery(b, "ktm")).toBe(true);
    expect(matchesBranchQuery(b, " HUB ")).toBe(true);
    expect(matchesBranchQuery(b, "pokhara")).toBe(false);
    expect(matchesBranchQuery(b, "   ")).toBe(true);
  });

  it("filters by type, query and the active-only default", () => {
    const branches = [
      branch({ id: "a" }),
      branch({ id: "b", type: "Branch", code: "PKR", name: "Pokhara" }),
      branch({ id: "c", isActive: false }),
    ];
    expect(filterBranches(branches, { q: "", showInactive: false }).map((b) => b.id)).toEqual(["a", "b"]);
    expect(filterBranches(branches, { q: "", showInactive: true }).map((b) => b.id)).toEqual(["a", "b", "c"]);
    expect(filterBranches(branches, { type: "Branch", q: "", showInactive: false }).map((b) => b.id)).toEqual(["b"]);
    expect(filterBranches(branches, { q: "pkr", showInactive: false }).map((b) => b.id)).toEqual(["b"]);
  });

  it("links the right-now cards to filtered lists", () => {
    expect(onHandShipmentsHref("b1")).toBe(
      "/shipments?branchId=b1&status=AtOriginBranch&status=Sorted&status=AtDestinationBranch&status=RtoInitiated&status=RtoAtOrigin",
    );
    expect(outForDeliveryHref("b1")).toBe("/shipments?branchId=b1&status=OutForDelivery");
    expect(ridersOutHref("b1")).toBe("/riders?branch=b1&stage=on");
    expect(zeroActivity("b1")).toEqual({ branchId: "b1", onHand: 0, outForDelivery: 0, ridersOut: 0, openExceptions: 0 });
  });
});
