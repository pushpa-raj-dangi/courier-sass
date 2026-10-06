import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { toApiQuery, type ShipmentListParams } from "@/features/shipments/lib/listParams";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/shipments",
  useSearchParams: () => new URLSearchParams(search),
}));

const useShipmentsMock = vi.fn();
vi.mock("@/features/shipments/hooks/useShipments", () => ({
  useShipments: (params: ShipmentListParams) => useShipmentsMock(params),
}));
vi.mock("@/features/shipments/hooks/useShipmentsSummary", () => ({
  useShipmentsSummary: () => ({ data: { InTransit: 4, DeliveryFailed: 1, NdrCreated: 2 }, isLoading: false, isError: false }),
}));
vi.mock("@/features/shipments/hooks/useServiceTypes", () => ({ useServiceTypes: () => ({ data: [{ id: "s1", code: "Express", name: "Express" }] }) }));
vi.mock("@/features/shipments/hooks/useMerchants", () => ({ useMerchants: () => ({ data: [{ id: "m1", displayName: "Everest Organics", isActive: true }] }) }));
vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b1", code: "KTM", name: "Kathmandu Hub" }, { id: "b2", code: "LAL", name: "Lalitpur" }] }),
}));
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: () => true }));
const openDetail = vi.fn();
vi.mock("@/features/shipments/store/shipmentPanelStore", () => ({
  useShipmentPanelStore: (selector: (s: object) => unknown) =>
    selector({ openCreate: vi.fn(), openDetail, panel: { mode: "closed" } }),
}));

import { ShipmentsView } from "./ShipmentsView";

const row = {
  id: "x1", awb: "QA-1", merchantId: "m1", originBranchId: "b1", destinationBranchId: "b2", currentBranchId: "b2",
  currentCustodianUserId: null, currentCustodianRiderId: null, currentStatus: "InTransit", serviceTypeId: "s1",
  weightKg: 1, lengthCm: null, widthCm: null, heightCm: null, declaredValue: 0, codAmount: 0, wantsInsurance: false,
  deliveryCharge: 0, insuranceCharge: 0, otherCharges: 0, totalCharge: 0, recipientName: "Sita", recipientPhone: "98",
  recipientAddressLine: "Jawalakhel", senderName: "S", senderPhone: "1", senderAddressLine: "A", items: [],
  createdAt: "2026-10-05T05:00:00Z", lastEventAt: null,
};

function page(items: unknown[]) {
  return { data: { items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 }, isLoading: false, isError: false, refetch: vi.fn() };
}

describe("ShipmentsView", () => {
  beforeEach(() => {
    search = "";
    replace.mockReset();
    openDetail.mockReset();
    useShipmentsMock.mockReset().mockReturnValue(page([row]));
  });

  it("shows tab counts from the summary", () => {
    render(<ShipmentsView />);
    expect(screen.getByRole("tab", { name: /NDR\s*3/ })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /In transit\s*4/ })).toBeInTheDocument();
  });

  it("reads the active tab from the URL and passes it to the query", () => {
    search = "tab=ndr";
    render(<ShipmentsView />);
    expect(screen.getByRole("tab", { name: /NDR/ })).toHaveAttribute("aria-selected", "true");
    expect(useShipmentsMock).toHaveBeenLastCalledWith(expect.objectContaining({ tab: "ndr" }));
  });

  it("selecting a tab writes it to the URL and resets the page", () => {
    search = "page=3";
    render(<ShipmentsView />);
    fireEvent.mouseDown(screen.getByRole("tab", { name: /RTO/ }));
    expect(replace).toHaveBeenCalledWith("/shipments?tab=rto", { scroll: false });
  });

  it("debounces search before writing it to the URL", () => {
    vi.useFakeTimers();
    render(<ShipmentsView />);
    fireEvent.change(screen.getByRole("searchbox", { name: /filter shipments/i }), { target: { value: "sita" } });
    expect(replace).not.toHaveBeenCalled();
    act(() => vi.advanceTimersByTime(300));
    expect(replace).toHaveBeenCalledWith("/shipments?q=sita", { scroll: false });
    vi.useRealTimers();
  });

  it("renders route codes and opens the sheet on row click", () => {
    render(<ShipmentsView />);
    expect(screen.getByText("KTM → LAL")).toBeInTheDocument();
    expect(screen.getByText("Prepaid")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "QA-1" }));
    expect(openDetail).toHaveBeenCalledWith("QA-1");
  });

  it("distinguishes a filtered-empty result and offers to clear filters", () => {
    search = "merchantId=m1";
    useShipmentsMock.mockReturnValue(page([]));
    render(<ShipmentsView />);
    expect(screen.getByText("No shipments match these filters")).toBeInTheDocument();
    fireEvent.click(within(screen.getByRole("toolbar")).getByRole("button", { name: /clear filters/i }));
    expect(replace).toHaveBeenCalledWith("/shipments", { scroll: false });
  });

  it("describes the total as a scope count without filters and a match count with them", () => {
    const { unmount } = render(<ShipmentsView />);
    expect(screen.getByText("1 shipment in your branch scope")).toBeInTheDocument();
    unmount();
    search = "merchantId=m1";
    useShipmentsMock.mockReturnValue(page([row, { ...row, id: "x2", awb: "QA-2" }]));
    render(<ShipmentsView />);
    expect(screen.getByText("2 shipments match these filters")).toBeInTheDocument();
  });

  it("explicit status params override the tab", () => {
    search = "tab=ndr&status=Created&status=PickupAssigned";
    render(<ShipmentsView />);
    screen.getAllByRole("tab").forEach((t) => expect(t).not.toHaveAttribute("aria-selected", "true"));
    expect(useShipmentsMock).toHaveBeenLastCalledWith(expect.objectContaining({ statuses: ["Created", "PickupAssigned"] }));
  });

  it("does not send a 1-character search to the API", () => {
    vi.useFakeTimers();
    render(<ShipmentsView />);
    fireEvent.change(screen.getByRole("searchbox", { name: /filter shipments/i }), { target: { value: "a" } });
    act(() => vi.advanceTimersByTime(300));
    const lastParams = useShipmentsMock.mock.calls.at(-1)![0] as ShipmentListParams;
    expect(toApiQuery(lastParams, 20).has("q")).toBe(false);
    vi.useRealTimers();
  });

  it("changing a filter resets the page", () => {
    search = "page=3";
    render(<ShipmentsView />);
    fireEvent.click(screen.getByRole("button", { name: /Merchant/ }));
    fireEvent.click(screen.getByRole("option", { name: /Everest Organics/ }));
    expect(replace).toHaveBeenCalledWith("/shipments?merchantId=m1", { scroll: false });
  });

  it("keeps newer keystrokes while the URL catches up", () => {
    vi.useFakeTimers();
    const { rerender } = render(<ShipmentsView />);
    const input = () => screen.getByRole("searchbox", { name: /filter shipments/i }) as HTMLInputElement;
    fireEvent.change(input(), { target: { value: "ab" } });
    act(() => vi.advanceTimersByTime(300));
    expect(replace).toHaveBeenCalledWith("/shipments?q=ab", { scroll: false });
    fireEvent.change(input(), { target: { value: "abc" } });
    search = "q=ab";
    rerender(<ShipmentsView />);
    expect(input().value).toBe("abc");
    vi.useRealTimers();
  });

  it("re-applies the same search text typed again after Clear filters", () => {
    vi.useFakeTimers();
    search = "q=ab";
    const { rerender } = render(<ShipmentsView />);
    fireEvent.click(within(screen.getByRole("toolbar")).getByRole("button", { name: /clear filters/i }));
    expect(replace).toHaveBeenCalledWith("/shipments", { scroll: false });
    replace.mockClear();
    search = "";
    rerender(<ShipmentsView />);
    const input = screen.getByRole("searchbox", { name: /filter shipments/i }) as HTMLInputElement;
    expect(input.value).toBe("");
    fireEvent.change(input, { target: { value: "ab" } });
    act(() => vi.advanceTimersByTime(300));
    expect(replace).toHaveBeenCalledWith("/shipments?q=ab", { scroll: false });
    vi.useRealTimers();
  });

  it("shows the plain empty state with no filters", () => {
    useShipmentsMock.mockReturnValue(page([]));
    render(<ShipmentsView />);
    expect(screen.getByText("No shipments yet")).toBeInTheDocument();
  });
});
