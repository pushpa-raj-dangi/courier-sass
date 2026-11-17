import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import MerchantDashboardPage from "@/app/merchant/dashboard/page";

const openDetail = vi.fn();
const openCreateShipment = vi.fn();
const openCreatePickup = vi.fn();
let perms = new Set<string>();
let counts: Record<string, { count: number | null; isLoading: boolean }> = {};

vi.mock("@/features/merchant-auth/hooks/useMerchantSession", () => ({
  useMerchantSession: () => ({ data: { fullName: "Asha Gurung", merchantDisplayName: "Himal Crafts" } }),
}));
vi.mock("@/features/merchant-auth/hooks/useMerchantPermission", () => ({
  useMerchantPermission: (p: string) => perms.has(p),
}));
vi.mock("@/features/merchant-shipments/hooks/useMerchantShipmentCounts", () => ({
  useMerchantShipmentCounts: () => counts,
}));
vi.mock("@/features/merchant-shipments/hooks/useMerchantShipments", () => ({
  useMerchantShipments: () => ({
    data: {
      items: [
        { id: "1", awb: "AWB-001", recipientName: "Ram", currentStatus: "InTransit", codAmount: 1200, createdAt: "2026-11-10T05:00:00Z" },
        { id: "2", awb: "AWB-002", recipientName: "Sita", currentStatus: "Delivered", codAmount: 0, createdAt: "2026-11-11T05:00:00Z" },
      ],
      page: 1, pageSize: 7, totalCount: 2, totalPages: 1,
    },
    isLoading: false,
    isError: false,
  }),
}));
vi.mock("@/features/merchant-cod/hooks/useMerchantCodSummary", () => ({
  useMerchantCodSummary: () => ({
    data: {
      collectedNotSettled: { amount: 61450, count: 12 },
      inSettlement: { amount: 8000, count: 1 },
      settledThisMonth: { amount: 90000, payouts: 3, lastSettledAt: "2026-11-05T05:00:00Z" },
      deliveredToday: { parcels: 4, codCollected: 5300 },
    },
    isLoading: false,
    isError: false,
  }),
}));
vi.mock("@/features/merchant-shipments/store/merchantShipmentPanelStore", () => ({
  useMerchantShipmentPanelStore: (sel: (s: unknown) => unknown) => sel({ openDetail, openCreate: openCreateShipment }),
}));
vi.mock("@/features/merchant-pickups/store/merchantPickupPanelStore", () => ({
  useMerchantPickupPanelStore: (sel: (s: unknown) => unknown) => sel({ openCreate: openCreatePickup }),
}));

const c = (count: number | null) => ({ count, isLoading: false });

beforeEach(() => {
  vi.clearAllMocks();
  perms = new Set(["merchant.cod.read", "shipment.create", "merchant.pickups.create"]);
  counts = {
    all: c(30), booked: c(3), inTransit: c(5), outForDelivery: c(2),
    needsAttention: c(1), returning: c(2), delivered: c(9),
  };
});

describe("merchant dashboard", () => {
  it("greets by first name and summarises parcels on the way", () => {
    render(<MerchantDashboardPage />);
    expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent(/^Good (morning|afternoon|evening), Asha$/);
    expect(screen.getByText("Himal Crafts · 10 parcels on the way")).toBeInTheDocument();
  });

  it("shows a dash when a count needed for the description failed", () => {
    counts.booked = c(null);
    render(<MerchantDashboardPage />);
    expect(screen.getByText("Himal Crafts · — parcels on the way")).toBeInTheDocument();
  });

  it("renders the money band with formatted amounts and captions", () => {
    render(<MerchantDashboardPage />);
    expect(screen.getByText("Collected, not yet settled")).toBeInTheDocument();
    expect(screen.getByText("Rs 61,450")).toBeInTheDocument();
    expect(screen.getByText("12 delivered COD parcels")).toBeInTheDocument();
    expect(screen.getByText("1 settlement awaiting payout")).toBeInTheDocument();
    expect(screen.getByText(/^3 payouts · last on /)).toBeInTheDocument();
  });

  it("hides the money band without merchant.cod.read", () => {
    perms.delete("merchant.cod.read");
    render(<MerchantDashboardPage />);
    expect(screen.queryByText("Collected, not yet settled")).not.toBeInTheDocument();
  });

  it("renders parcel tiles with counts and tab links", () => {
    render(<MerchantDashboardPage />);
    expect(screen.getByRole("link", { name: /Booked/ })).toHaveAttribute("href", "/merchant/shipments?tab=booked");
    expect(screen.getByRole("link", { name: /In transit/ })).toHaveAttribute("href", "/merchant/shipments?tab=inTransit");
    expect(screen.getByRole("link", { name: /Out for delivery/ })).toHaveAttribute("href", "/merchant/shipments?tab=outForDelivery");
    const delivered = screen.getByRole("link", { name: /Delivered today/ });
    expect(delivered).toHaveAttribute("href", "/merchant/shipments?tab=delivered");
    expect(delivered).toHaveTextContent("4");
    expect(delivered).toHaveTextContent("Rs 5,300 COD collected");
  });

  it("lists attention items linking to their tabs", () => {
    render(<MerchantDashboardPage />);
    expect(screen.getByRole("link", { name: "1 failed deliveries" })).toHaveAttribute("href", "/merchant/shipments?tab=needsAttention");
    expect(screen.getByRole("link", { name: "2 returning to you" })).toHaveAttribute("href", "/merchant/shipments?tab=returning");
  });

  it("says nothing needs attention when both counts are zero", () => {
    counts.needsAttention = c(0);
    counts.returning = c(0);
    render(<MerchantDashboardPage />);
    expect(screen.getByText("Nothing needs you right now")).toBeInTheDocument();
  });

  it("opens the detail sheet from a recent shipment row", () => {
    render(<MerchantDashboardPage />);
    fireEvent.click(screen.getByRole("button", { name: "AWB-002" }));
    expect(openDetail).toHaveBeenCalledWith("AWB-002");
  });

  it("wires header actions to the create sheets", () => {
    render(<MerchantDashboardPage />);
    fireEvent.click(screen.getByRole("button", { name: /Book shipment/ }));
    fireEvent.click(screen.getByRole("button", { name: /Request pickup/ }));
    expect(openCreateShipment).toHaveBeenCalled();
    expect(openCreatePickup).toHaveBeenCalled();
  });
});
