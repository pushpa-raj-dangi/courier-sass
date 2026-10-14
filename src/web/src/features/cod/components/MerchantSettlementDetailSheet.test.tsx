import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { formatRs } from "@/features/cod/lib/codFormat";

let settlement: unknown;
vi.mock("@/features/cod/hooks/useMerchantSettlement", () => ({
  useMerchantSettlement: () => ({ data: settlement, isLoading: false, isError: false, refetch: vi.fn() }),
}));
vi.mock("@/features/cod/hooks/useAdjustments", () => ({
  useAdjustments: () => ({ data: { items: [{ id: "a1", entityType: "MerchantSettlementItem", entityId: "i1", beforeValue: 0, afterValue: 50, reason: "Damaged", requestedByUserId: "u", requestedAt: "2026-10-06T00:00:00Z", approvedByUserId: "u2", approvedAt: "2026-10-06T01:00:00Z", applied: true }] } }),
}));
vi.mock("@/features/shipments/hooks/useMerchants", () => ({ useMerchants: () => ({ data: [{ id: "m1", displayName: "Everest Organics" }] }) }));
const settle = vi.fn();
vi.mock("@/features/cod/hooks/useSettleMerchantSettlement", () => ({ useSettleMerchantSettlement: () => ({ mutateAsync: settle, isPending: false }) }));
vi.mock("@/features/cod/hooks/useRequestAdjustment", () => ({ useRequestAdjustment: () => ({ mutateAsync: vi.fn() }) }));
let perms = new Set(["cod.read", "cod.settle", "cod.adjust"]);
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => perms.has(p) }));
vi.mock("@/features/cod/store/codPanelStore", () => ({
  useCodPanelStore: (sel: (s: unknown) => unknown) => sel({ panel: { mode: "merchant-settlement-detail", id: "ms1" }, close: vi.fn() }),
}));

import { MerchantSettlementDetailSheet } from "./MerchantSettlementDetailSheet";

const base = {
  id: "ms1", merchantId: "m1", periodFrom: "2026-10-01T00:00:00Z", periodTo: "2026-10-07T00:00:00Z", status: "Created", settledAt: null,
  items: [
    { id: "i1", shipmentId: "s1", shipmentAwb: "NX-00148", codAmount: 2500, courierFee: 150, otherDeductions: 50, netPayable: 2300 },
    { id: "i2", shipmentId: "s2", shipmentAwb: "NX-00149", codAmount: 1000, courierFee: 100, otherDeductions: 0, netPayable: 900 },
  ],
};

function renderSheet() {
  return render(<QueryClientProvider client={new QueryClient()}><MerchantSettlementDetailSheet /></QueryClientProvider>);
}

beforeEach(() => {
  perms = new Set(["cod.read", "cod.settle", "cod.adjust"]);
  settlement = { ...base, totalPayable: 3200 };
});

describe("MerchantSettlementDetailSheet", () => {
  it("shows the breakdown ending in the server payable", () => {
    renderSheet();
    expect(screen.getByText("Everest Organics")).toBeInTheDocument();
    const breakdown = screen.getByRole("list", { name: "Payable breakdown" });
    expect(within(breakdown).getByText("2 delivered COD parcels")).toBeInTheDocument();
    expect(within(breakdown).getByText(formatRs(3500))).toBeInTheDocument();
    expect(within(breakdown).getByText(`− ${formatRs(250)}`)).toBeInTheDocument();
    expect(within(breakdown).getByText(`Includes ${formatRs(50)} from approved adjustments`)).toBeInTheDocument();
    expect(within(breakdown).getByText(formatRs(3200))).toBeInTheDocument();
    expect(screen.queryByText(/don't add up/)).toBeNull();
  });

  it("warns when lines don't add up to the payable", () => {
    settlement = { ...base, totalPayable: 3000 };
    renderSheet();
    expect(screen.getByText(/Lines don't add up to the payable/)).toBeInTheDocument();
  });

  it("traces a selected line", () => {
    renderSheet();
    fireEvent.click(screen.getByRole("button", { name: "Trace NX-00149" }));
    const trace = screen.getByRole("region", { name: "Trace one line" });
    expect(within(trace).getByText("NX-00149")).toBeInTheDocument();
    expect(within(trace).getByText(formatRs(900))).toBeInTheDocument();
  });

  it("hides settle and adjust actions without permissions", () => {
    perms = new Set(["cod.read"]);
    renderSheet();
    expect(screen.queryByRole("button", { name: "Mark settled" })).toBeNull();
    expect(screen.queryByRole("button", { name: /Request adjustment/ })).toBeNull();
  });
});
