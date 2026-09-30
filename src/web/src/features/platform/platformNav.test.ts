import { describe, expect, it } from "vitest";
import { findActiveItem } from "@/components/console/navModel";
import { PLATFORM_NAV } from "./platformNav";

describe("PLATFORM_NAV", () => {
  it("groups every platform page under Tenants, Reference and System", () => {
    expect(PLATFORM_NAV.map((g) => [g.label, g.items.map((i) => i.label)])).toEqual([
      ["Tenants", ["Dashboard", "Organizations", "Plans", "Invoices"]],
      ["Reference", ["Countries"]],
      ["System", ["Jobs", "Queues", "Audit Logs", "Settings"]],
    ]);
  });

  it("highlights a section for its nested routes", () => {
    expect(findActiveItem("/platform/organizations/abc", PLATFORM_NAV)?.item.label).toBe("Organizations");
    expect(findActiveItem("/platform/plans", PLATFORM_NAV)?.item.label).toBe("Plans");
  });
});
