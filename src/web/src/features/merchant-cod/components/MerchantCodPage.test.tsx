import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/merchant/cod",
  useSearchParams: () => new URLSearchParams(search),
}));

const summaryMock = vi.fn();
const awaitingMock = vi.fn();
const settlementsMock = vi.fn();
const settlementMock = vi.fn();
vi.mock("@/features/merchant-cod/hooks/useMerchantCodSummary", () => ({ useMerchantCodSummary: () => summaryMock() }));
vi.mock("@/features/merchant-cod/hooks/useMerchantAwaiting", () => ({ useMerchantAwaiting: (p: number) => awaitingMock(p) }));
vi.mock("@/features/merchant-cod/hooks/useMerchantSettlements", () => ({ useMerchantSettlements: () => settlementsMock() }));
vi.mock("@/features/merchant-cod/hooks/useMerchantSettlement", () => ({ useMerchantSettlement: (id: string) => settlementMock(id) }));

import { useMerchantSettlementPanelStore } from "@/features/merchant-cod/store/merchantSettlementPanelStore";
import { MerchantCodPage } from "./MerchantCodPage";
import { MerchantSettlementDetailSheet } from "./MerchantSettlementDetailSheet";

const recent = new Date(Date.now() - 5 * 86_400_000).toISOString();
const settlement = {
  id: "st1", merchantId: "m1", periodFrom: recent, periodTo: recent, totalPayable: 3230, status: "Created", settledAt: null,
  items: [
    { id: "i1", shipmentId: "s1", shipmentAwb: "QA-100", codAmount: 2500, courierFee: 150, otherDeductions: 0, netPayable: 2350 },
    { id: "i2", shipmentId: "s2", shipmentAwb: "QA-101", codAmount: 1000, courierFee: 100, otherDeductions: 20, netPayable: 880 },
  ],
};
const ok = (data: unknown) => ({ data, isLoading: false, isError: false, refetch: vi.fn() });

function renderPage() {
  return render(
    <>
      <MerchantCodPage />
      <MerchantSettlementDetailSheet />
    </>
  );
}

beforeEach(() => {
  search = "";
  replace.mockReset();
  useMerchantSettlementPanelStore.setState({ panel: { mode: "closed" } });
  summaryMock.mockReturnValue(
    ok({
      collectedNotSettled: { amount: 5000, count: 3 },
      inSettlement: { amount: 3230, count: 2 },
      settledThisMonth: { amount: 9000, payouts: 2, lastSettledAt: null },
      deliveredToday: { parcels: 1, codCollected: 0 },
    })
  );
  settlementsMock.mockReturnValue(ok([settlement]));
  awaitingMock.mockReturnValue(
    ok({
      items: [{ shipmentId: "s9", awb: "QA-900", collectedAt: recent, collectedAmount: 1200, courierFee: 100, net: 1100 }],
      page: 1, pageSize: 20, totalCount: 1, totalPages: 1,
    })
  );
  settlementMock.mockReturnValue(ok(undefined));
});

describe("MerchantCodPage", () => {
  it("shows the settlements table by default", () => {
    renderPage();
    expect(screen.getByRole("columnheader", { name: "You receive" })).toBeInTheDocument();
    expect(screen.getByText("Awaiting payout")).toBeInTheDocument();
  });

  it("shows the awaiting table for ?tab=awaiting", () => {
    search = "tab=awaiting";
    renderPage();
    expect(screen.getByText("QA-900")).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Net" })).toBeInTheDocument();
  });

  it("opens the sheet with the net total and a CSV download link", () => {
    settlementMock.mockReturnValue(ok(settlement));
    renderPage();
    fireEvent.click(screen.getAllByRole("row")[1]);
    expect(useMerchantSettlementPanelStore.getState().panel).toEqual({ mode: "detail", id: "st1" });
    expect(within(screen.getByRole("dialog")).getByText("You receive").nextSibling?.textContent).toBe("Rs 3,230");
    const link = screen.getByRole("link", { name: /Download CSV/ });
    expect(link).toHaveAttribute("href", "/api/merchant/cod/settlements/st1/export");
    expect(screen.getByText("2 delivered COD parcels")).toBeInTheDocument();
  });

  it("keeps an accessible sheet title while loading", () => {
    useMerchantSettlementPanelStore.setState({ panel: { mode: "detail", id: "st1" } });
    settlementMock.mockReturnValue({ data: undefined, isLoading: true, isError: false, refetch: vi.fn() });
    renderPage();
    expect(screen.getByRole("dialog", { name: "Settlement" })).toBeInTheDocument();
  });

  it("shows em dashes in the strip when the summary fails", () => {
    summaryMock.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn() });
    renderPage();
    expect(screen.getAllByText("—").length).toBeGreaterThanOrEqual(3);
    expect(screen.getByRole("button", { name: "Try again" })).toBeInTheDocument();
  });

  it("shows skeletons while the summary loads", () => {
    summaryMock.mockReturnValue({ data: undefined, isLoading: true, isError: false, refetch: vi.fn() });
    renderPage();
    expect(screen.getAllByTestId("cod-strip-skeleton")).toHaveLength(3);
  });

  it("shows the settlements empty state", () => {
    settlementsMock.mockReturnValue(ok([]));
    renderPage();
    expect(screen.getByText("No settlements yet")).toBeInTheDocument();
  });

  it("shows the settlements error state with retry", () => {
    const refetch = vi.fn();
    settlementsMock.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch });
    renderPage();
    fireEvent.click(screen.getAllByRole("button", { name: "Try again" })[0]);
    expect(refetch).toHaveBeenCalled();
  });

  it("shows the awaiting empty and error states", () => {
    search = "tab=awaiting";
    awaitingMock.mockReturnValue(ok({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 1 }));
    const { unmount } = renderPage();
    expect(screen.getByText("Nothing awaiting settlement")).toBeInTheDocument();
    unmount();
    awaitingMock.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn() });
    renderPage();
    expect(screen.getAllByRole("button", { name: "Try again" }).length).toBeGreaterThan(0);
  });
});
