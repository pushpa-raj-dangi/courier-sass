import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MERCHANT_STATUS_GROUPS } from "@/features/merchant-shipments/lib/statusGroups";
import { toMerchantApiQuery, type MerchantShipmentListParams } from "@/features/merchant-shipments/lib/listParams";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/merchant/shipments",
  useSearchParams: () => new URLSearchParams(search),
}));

const useShipmentsMock = vi.fn();
vi.mock("@/features/merchant-shipments/hooks/useMerchantShipments", () => ({
  useMerchantShipments: (params: MerchantShipmentListParams) => useShipmentsMock(params),
}));
let counts: Record<string, { count: number | null; isLoading: boolean }> = {};
vi.mock("@/features/merchant-shipments/hooks/useMerchantShipmentCounts", () => ({
  useMerchantShipmentCounts: () => counts,
}));
vi.mock("@/features/merchant-shipments/hooks/useMerchantBranches", () => ({
  useMerchantBranches: () => ({ data: [{ id: "b2", code: "LAL", name: "Lalitpur" }] }),
}));
vi.mock("@/features/merchant-shipments/hooks/useMerchantServiceTypes", () => ({
  useMerchantServiceTypes: () => ({ data: [{ id: "s1", code: "Express", name: "Express" }] }),
}));
vi.mock("@/features/merchant-auth/hooks/useMerchantPermission", () => ({ useMerchantPermission: () => true }));
const openDetail = vi.fn();
vi.mock("@/features/merchant-shipments/store/merchantShipmentPanelStore", () => ({
  useMerchantShipmentPanelStore: (sel: (s: unknown) => unknown) => sel({ openDetail, openCreate: vi.fn() }),
}));
vi.mock("@/features/merchant-pickups/store/merchantPickupPanelStore", () => ({
  useMerchantPickupPanelStore: (sel: (s: unknown) => unknown) => sel({ openCreate: vi.fn() }),
}));

import { MerchantShipmentsView } from "./MerchantShipmentsView";

const row = {
  id: "x1", awb: "QA-1", merchantId: "m1", originBranchId: "b1", destinationBranchId: "b2", currentBranchId: "b2",
  currentCustodianUserId: null, currentCustodianRiderId: null, currentStatus: "InTransit", serviceTypeId: "s1",
  weightKg: 1, lengthCm: null, widthCm: null, heightCm: null, declaredValue: 0, codAmount: 0, wantsInsurance: false,
  deliveryCharge: 100, insuranceCharge: 0, otherCharges: 0, totalCharge: 150, recipientName: "Sita", recipientPhone: "98",
  recipientAddressLine: "Jawalakhel", senderName: "S", senderPhone: "1", senderAddressLine: "A", items: [],
  createdAt: "2026-10-05T05:00:00Z", lastEventAt: null,
};

function page(items: unknown[]) {
  return { data: { items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 }, isLoading: false, isError: false, refetch: vi.fn() };
}
const c = (count: number | null) => ({ count, isLoading: false });
function allCounts(over: Record<string, ReturnType<typeof c>> = {}) {
  return {
    all: c(10), booked: c(1), inTransit: c(3), outForDelivery: c(1), needsAttention: c(2), returning: c(0), delivered: c(3), ...over,
  };
}

function renderView() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const spy = vi.spyOn(qc, "invalidateQueries");
  render(<QueryClientProvider client={qc}><MerchantShipmentsView /></QueryClientProvider>);
  return { spy };
}

describe("MerchantShipmentsView", () => {
  beforeEach(() => {
    vi.useRealTimers();
    search = "";
    replace.mockReset();
    openDetail.mockReset();
    counts = allCounts();
    useShipmentsMock.mockReset().mockReturnValue(page([row]));
  });

  it("selects the tab from the URL and requests that group's statuses", () => {
    search = "tab=needsAttention";
    renderView();
    expect(screen.getByRole("tab", { name: /Needs attention/ })).toHaveAttribute("aria-selected", "true");
    const params = useShipmentsMock.mock.calls.at(-1)![0] as MerchantShipmentListParams;
    expect(toMerchantApiQuery(params, 20).getAll("status")).toEqual([...MERCHANT_STATUS_GROUPS.needsAttention.statuses]);
  });

  it("debounces search into the URL q and resets the page", () => {
    vi.useFakeTimers();
    search = "page=3";
    renderView();
    fireEvent.change(screen.getByRole("searchbox", { name: /search shipments/i }), { target: { value: "sita" } });
    expect(replace).not.toHaveBeenCalled();
    act(() => vi.advanceTimersByTime(300));
    expect(replace).toHaveBeenCalledWith("/merchant/shipments?q=sita", { scroll: false });
  });

  it("selecting a tab writes it to the URL and resets the page", () => {
    search = "page=3";
    renderView();
    fireEvent.mouseDown(screen.getByRole("tab", { name: /Delivered/ }));
    expect(replace).toHaveBeenCalledWith("/merchant/shipments?tab=delivered", { scroll: false });
  });

  it("shows Prepaid for a zero-COD shipment and the destination branch name", () => {
    renderView();
    expect(screen.getByText("Prepaid")).toBeInTheDocument();
    expect(screen.getByText("Lalitpur")).toBeInTheDocument();
  });

  it("shows a dash for an unknown destination branch and Rs for COD", () => {
    useShipmentsMock.mockReturnValue(page([{ ...row, destinationBranchId: "zzz", codAmount: 2500 }]));
    renderView();
    expect(screen.getByText("Rs 2,500")).toBeInTheDocument();
    expect(screen.getByTestId("destination-x1")).toHaveTextContent("—");
  });

  it("opens the detail sheet when a row is clicked", () => {
    renderView();
    fireEvent.click(screen.getByText("Sita"));
    expect(openDetail).toHaveBeenCalledWith("QA-1");
  });

  it("renders a null tab count as a dash, never 0", () => {
    counts = allCounts({ inTransit: c(null) });
    renderView();
    expect(screen.getByRole("tab", { name: /In transit\s*—/ })).toBeInTheDocument();
  });

  it("offers a retry for failed counts that invalidates the count queries", () => {
    counts = allCounts({ inTransit: c(null) });
    const { spy } = renderView();
    fireEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect(spy).toHaveBeenCalledWith({ queryKey: ["merchant-shipments", "count"] });
  });

  it("shows count skeletons while counts load", () => {
    counts = allCounts({ booked: { count: null, isLoading: true } });
    renderView();
    expect(screen.getAllByTestId("tab-count-skeleton")).toHaveLength(1);
  });

  it("describes the header from the counts", () => {
    renderView();
    expect(screen.getByText("10 shipments booked · 5 on the way")).toBeInTheDocument();
  });

  it("header shows a dash when a needed count failed", () => {
    counts = allCounts({ all: c(null) });
    renderView();
    expect(screen.getByText("— shipments booked · 5 on the way")).toBeInTheDocument();
  });

  it("shows the empty state", () => {
    useShipmentsMock.mockReturnValue(page([]));
    renderView();
    expect(screen.getByText("No shipments here yet")).toBeInTheDocument();
  });

  it("shows the loading skeleton", () => {
    useShipmentsMock.mockReturnValue({ data: undefined, isLoading: true, isError: false, refetch: vi.fn() });
    renderView();
    expect(screen.getByRole("status")).toBeInTheDocument();
  });

  it("shows an error state with retry", () => {
    const refetch = vi.fn();
    useShipmentsMock.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch });
    renderView();
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(refetch).toHaveBeenCalled();
  });
});
