import { describe, expect, it } from "vitest";
import { Package } from "lucide-react";
import { findActiveItem, groupNavItems } from "./navModel";
import type { ConsoleNavGroup } from "./types";

const item = (href: string, group: string, matchPrefix = true) => ({ href, label: href, icon: Package, group, matchPrefix });

describe("groupNavItems", () => {
  it("orders groups by the definition order and keeps item order", () => {
    const groups = groupNavItems(
      [item("/cod", "finance"), item("/dashboard", "ops"), item("/shipments", "ops")],
      [{ id: "ops", label: "Operations" }, { id: "finance", label: "Finance" }]
    );
    expect(groups.map((g) => g.label)).toEqual(["Operations", "Finance"]);
    expect(groups[0].items.map((i) => i.href)).toEqual(["/dashboard", "/shipments"]);
  });

  it("drops groups with no items (no orphan headings)", () => {
    const groups = groupNavItems([item("/dashboard", "ops")], [
      { id: "ops", label: "Operations" },
      { id: "admin", label: "Admin" },
    ]);
    expect(groups.map((g) => g.id)).toEqual(["ops"]);
  });

  it("returns no groups for no items", () => {
    expect(groupNavItems([], [{ id: "ops", label: "Operations" }])).toEqual([]);
  });

  it("strips the group key from items", () => {
    const [group] = groupNavItems([item("/dashboard", "ops")], [{ id: "ops", label: "Operations" }]);
    expect(group.items[0]).not.toHaveProperty("group");
  });
});

describe("findActiveItem", () => {
  const nav: ConsoleNavGroup[] = groupNavItems(
    [
      item("/dashboard", "ops"),
      item("/riders", "ops"),
      item("/riders/live-map", "ops"),
      item("/billing", "finance"),
      item("/exact-only", "finance", false),
    ],
    [{ id: "ops", label: "Operations" }, { id: "finance", label: "Finance" }]
  );

  it("matches an exact href", () => {
    expect(findActiveItem("/dashboard", nav)?.item.href).toBe("/dashboard");
  });

  it("prefers the most specific (longest) match for nested routes", () => {
    expect(findActiveItem("/riders/live-map", nav)?.item.href).toBe("/riders/live-map");
    expect(findActiveItem("/riders", nav)?.item.href).toBe("/riders");
  });

  it("matches descendants of a prefix item", () => {
    const active = findActiveItem("/billing/callback/abc", nav);
    expect(active?.item.href).toBe("/billing");
    expect(active?.group.label).toBe("Finance");
  });

  it("does not treat a shared string prefix as a descendant", () => {
    expect(findActiveItem("/ridersx", nav)).toBeUndefined();
  });

  it("does not prefix-match items without matchPrefix", () => {
    expect(findActiveItem("/exact-only/child", nav)).toBeUndefined();
  });

  it("returns undefined for an unknown path", () => {
    expect(findActiveItem("/nowhere", nav)).toBeUndefined();
  });
});
