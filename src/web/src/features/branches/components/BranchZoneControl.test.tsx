import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import type { Branch } from "@/features/branches/types";

let granted: string[] = [];
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => granted.includes(p) }));
vi.mock("@/features/geography/hooks/useDeliveryZones", () => ({ useDeliveryZones: () => ({ data: [{ id: "z1", name: "Valley" }] }) }));
vi.mock("@/features/branches/hooks/useUpdateDeliveryZone", () => ({
  useUpdateDeliveryZone: () => ({ mutate: vi.fn(), isPending: false, isError: false }),
}));

import { BranchZoneControl } from "./BranchZoneControl";

const branch = { id: "b1", deliveryZoneId: "z1" } as Branch;

describe("BranchZoneControl", () => {
  it("offers the zone picker with branch.update", () => {
    granted = ["branch.update"];
    render(<BranchZoneControl branch={branch} />);
    expect(screen.getByRole("combobox", { name: "Delivery zone" })).toBeEnabled();
  });

  it("is hidden without branch.update", () => {
    granted = [];
    render(<BranchZoneControl branch={branch} />);
    expect(screen.queryByRole("combobox", { name: "Delivery zone" })).toBeNull();
  });
});
