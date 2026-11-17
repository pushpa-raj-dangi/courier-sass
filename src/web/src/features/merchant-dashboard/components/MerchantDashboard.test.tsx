import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
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
  useMerchantActiveReturnsCount: () => activeReturns,
}));
let activeReturns: { count: number | null; isLoading: boolean } = { count: 2, isLoading: false };
const recentItems = [
  { id: "1", awb: "AWB-001", recipientName: "Ram", currentStatus: "InTransit", codAmount: 1200, createdAt: "2026-11-10T05:00:00Z" },
  { id: "2", awb: "AWB-002", recipientName: "Sita", currentStatus: "Delivered", codAmount: 0, createdAt: "2026-11-11T05:00:00Z" },
];
const summaryData = {
  collectedNotSettled: { amount: 61450, count: 12 },
  inSettlement: { amount: 8000, count: 1 },
  settledThisMonth: { amount: 90000, payouts: 3, lastSettledAt: "2026-11-05T05:00:00Z" },
  deliveredToday: { parcels: 4, codCollected: 5300 },
};
const refetchSummary = vi.fn();
const summaryHook = vi.fn();
let recent: { items: typeof recentItems; isLoading: boolean } = { items: recentItems, isLoading: false };

vi.mock("@/features/merchant-shipments/hooks/useMerchantShipments", () => ({
  useMerchantShipments: () => ({
    data: { items: recent.items, page: 1, pageSize: 7, totalCount: recent.items.length, totalPages: 1 },
    isLoading: recent.isLoading,
    isError: false,
  }),
}));
vi.mock("@/features/merchant-cod/hooks/useMerchantCodSummary", () => ({
  useMerchantCodSummary: (opts?: { enabled?: boolean }) => summaryHook(opts),
}));
vi.mock("@/features/merchant-shipments/store/merchantShipmentPanelStore", () => ({
  useMerchantShipmentPanelStore: (sel: (s: unknown) => unknown) => sel({ openDetail, openCreate: openCreateShipment }),
}));
vi.mock("@/features/merchant-pickups/store/merchantPickupPanelStore", () => ({
  useMerchantPickupPanelStore: (sel: (s: unknown) => unknown) => sel({ openCreate: openCreatePickup }),
}));

const c = (count: number | null) => ({ count, isLoading: false });

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return { qc, ...render(<QueryClientProvider client={qc}><MerchantDashboardPage /></QueryClientProvider>) };
}

beforeEach(() => {
  vi.clearAllMocks();
  recent = { items: recentItems, isLoading: false };
  summaryHook.mockImplementation((opts?: { enabled?: boolean }) =>
    opts?.enabled === false
      ? { data: undefined, isLoading: false, isError: false, refetch: refetchSummary }
      : { data: summaryData, isLoading: false, isError: false, refetch: refetchSummary });
  perms = new Set(["merchant.cod.read", "shipment.create", "merchant.pickups.create"]);
  counts = {
    all: c(30), booked: c(3), inTransit: c(5), outForDelivery: c(2),
    needsAttention: c(1), returning: c(2), delivered: c(9),
  };
  activeReturns = c(2);
});

describe("merchant dashboard", () => {
  it("greets by first name and summarises parcels on the way", () => {
    renderPage();
    expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent(/^Good (morning|afternoon|evening), Asha$/);
    expect(screen.getByText("Himal Crafts · 10 parcels on the way")).toBeInTheDocument();
  });

  it("shows a dash when a count needed for the description failed", () => {
    counts.booked = c(null);
    renderPage();
    expect(screen.getByText("Himal Crafts · — parcels on the way")).toBeInTheDocument();
  });

  it("renders the money band with formatted amounts and captions", () => {
    renderPage();
    expect(screen.getByText("Collected, not yet settled")).toBeInTheDocument();
    expect(screen.getByText("Rs 61,450")).toBeInTheDocument();
    expect(screen.getByText("12 delivered COD parcels")).toBeInTheDocument();
    expect(screen.getByText("1 settlement awaiting payout")).toBeInTheDocument();
    expect(screen.getByText(/^3 payouts · last on /)).toBeInTheDocument();
  });

  it("hides the money band without merchant.cod.read", () => {
    perms.delete("merchant.cod.read");
    renderPage();
    expect(screen.queryByText("Collected, not yet settled")).not.toBeInTheDocument();
  });

  it("renders parcel tiles with counts and tab links", () => {
    renderPage();
    expect(screen.getByRole("link", { name: /Booked/ })).toHaveAttribute("href", "/merchant/shipments?tab=booked");
    expect(screen.getByRole("link", { name: /In transit/ })).toHaveAttribute("href", "/merchant/shipments?tab=inTransit");
    expect(screen.getByRole("link", { name: /Out for delivery/ })).toHaveAttribute("href", "/merchant/shipments?tab=outForDelivery");
    const delivered = screen.getByRole("link", { name: /Delivered today/ });
    expect(delivered).toHaveAttribute("href", "/merchant/shipments?tab=delivered");
    expect(delivered).toHaveTextContent("4");
    expect(delivered).toHaveTextContent("Rs 5,300 COD collected");
  });

  it("lists attention items linking to their tabs", () => {
    renderPage();
    expect(screen.getByRole("link", { name: "1 failed delivery" })).toHaveAttribute("href", "/merchant/shipments?tab=needsAttention");
    expect(screen.getByRole("link", { name: "2 returning to you" })).toHaveAttribute("href", "/merchant/shipments?tab=returning");
  });

  it("says nothing needs attention when failed deliveries and active returns are both zero", () => {
    counts.needsAttention = c(0);
    // Returning tab still counts terminal RtoDelivered parcels; the attention row must ignore them.
    counts.returning = c(5);
    activeReturns = c(0);
    renderPage();
    expect(screen.getByText("Nothing needs you right now")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /returning to you/ })).not.toBeInTheDocument();
  });

  it("uses the active-returns count for the returning row", () => {
    counts.returning = c(5);
    activeReturns = c(3);
    renderPage();
    expect(screen.getByRole("link", { name: "3 returning to you" })).toHaveAttribute("href", "/merchant/shipments?tab=returning");
  });

  it("shows retry when the active-returns count failed", () => {
    activeReturns = c(null);
    renderPage();
    expect(screen.queryByRole("link", { name: /returning to you/ })).not.toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Try again" }).length).toBeGreaterThanOrEqual(1);
  });

  it("singularises one delivered COD parcel and shows Prepaid for zero-COD recent rows", () => {
    summaryHook.mockReturnValue({
      data: { ...summaryData, collectedNotSettled: { amount: 500, count: 1 } },
      isLoading: false, isError: false, refetch: refetchSummary,
    });
    renderPage();
    expect(screen.getByText("1 delivered COD parcel")).toBeInTheDocument();
    const row = screen.getByRole("button", { name: "AWB-002" }).closest("tr")!;
    expect(row).toHaveTextContent("Prepaid");
  });

  it("opens the detail sheet from a recent shipment row", () => {
    renderPage();
    fireEvent.click(screen.getByRole("button", { name: "AWB-002" }));
    expect(openDetail).toHaveBeenCalledWith("AWB-002");
  });

  it("wires header actions to the create sheets", () => {
    renderPage();
    fireEvent.click(screen.getByRole("button", { name: /Book shipment/ }));
    fireEvent.click(screen.getByRole("button", { name: /Request pickup/ }));
    expect(openCreateShipment).toHaveBeenCalled();
    expect(openCreatePickup).toHaveBeenCalled();
  });

  it("shows retry when the COD summary fails and refetches on click", () => {
    summaryHook.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: refetchSummary });
    renderPage();
    const retries = screen.getAllByRole("button", { name: "Try again" });
    expect(retries.length).toBeGreaterThanOrEqual(1);
    expect(screen.queryByText("Rs 61,450")).not.toBeInTheDocument();
    fireEvent.click(retries[0]);
    expect(refetchSummary).toHaveBeenCalled();
  });

  it("shows retry instead of dash links when a count failed", () => {
    counts.needsAttention = c(null);
    renderPage();
    expect(screen.queryByText(/— failed deliver/)).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /failed deliver/ })).not.toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Try again" }).length).toBeGreaterThanOrEqual(1);
  });

  it("shows empty copy when there are no recent shipments", () => {
    recent = { items: [], isLoading: false };
    renderPage();
    expect(screen.getByText("No shipments yet — book your first one")).toBeInTheDocument();
  });

  it("shows skeletons while loading", () => {
    counts.needsAttention = { count: null, isLoading: true };
    counts.booked = { count: null, isLoading: true };
    recent = { items: [], isLoading: true };
    summaryHook.mockReturnValue({ data: undefined, isLoading: true, isError: false, refetch: refetchSummary });
    renderPage();
    expect(screen.getAllByTestId("dashboard-skeleton").length).toBeGreaterThanOrEqual(3);
  });

  it("without merchant.cod.read makes no summary request and shows the all-time Delivered tile", () => {
    perms.delete("merchant.cod.read");
    renderPage();
    expect(summaryHook).toHaveBeenCalledWith({ enabled: false });
    expect(screen.queryByText("Delivered today")).not.toBeInTheDocument();
    const tile = screen.getByRole("link", { name: /^Delivered/ });
    expect(tile).toHaveAttribute("href", "/merchant/shipments?tab=delivered");
    expect(tile).toHaveTextContent("9");
    expect(tile).not.toHaveTextContent("COD collected");
  });
});
