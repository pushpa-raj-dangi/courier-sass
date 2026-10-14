import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { formatRs } from "@/features/cod/lib/codFormat";

const now = Date.parse("2026-10-07T12:00:00Z");
const riderCash = vi.fn();
vi.mock("@/features/cod/hooks/useRiderCash", () => ({ useRiderCash: (...a: unknown[]) => riderCash(...a) }));
const riderSettlements = vi.fn();
vi.mock("@/features/cod/hooks/useRiderSettlements", () => ({ useRiderSettlements: (p: unknown) => riderSettlements(p) }));
let perms = new Set(["cod.read", "cod.reconcile"]);
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => perms.has(p) }));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ riderName: (id: string) => (id === "r1" ? "Bikash Tamang" : "Rider"), branchName: () => "Lalitpur Branch", userName: () => "Staff" }),
}));
const openCreate = vi.fn();
const openDetail = vi.fn();
vi.mock("@/features/cod/store/codPanelStore", () => ({
  useCodPanelStore: (sel: (s: unknown) => unknown) => sel({ openRiderSettlementCreate: openCreate, openRiderSettlementDetail: openDetail }),
}));

import { RiderDepositsTab } from "./RiderDepositsTab";

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(now);
  perms = new Set(["cod.read", "cod.reconcile"]);
  openCreate.mockReset();
  riderCash.mockReset();
  riderSettlements.mockReset();
  riderCash.mockReturnValue({
    data: {
      items: [
        { riderId: "r1", branchId: "b1", parcelCount: 14, amount: 38400, oldestCollectedAt: "2026-10-07T09:00:00Z", latestCollectedAt: "2026-10-07T11:00:00Z" },
        { riderId: "r2", branchId: "b1", parcelCount: 7, amount: 16900, oldestCollectedAt: "2026-10-06T10:00:00Z", latestCollectedAt: "2026-10-06T11:00:00Z" },
      ],
    },
    isLoading: false,
    isError: false,
  });
  riderSettlements.mockReturnValue({
    data: { items: [{ id: "rs1", riderId: "r1", branchId: "b1", declaredAmount: 37900, shipmentCount: 14, settledAt: "2026-10-07T10:00:00Z", confirmedByUserId: "u1", branchCashSettlementId: null, expectedAmount: 38400, variance: -500 }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 },
    isLoading: false,
    isError: false,
  });
});

describe("RiderDepositsTab", () => {
  it("lists holdings longest-held first with an over-24 h warning", () => {
    render(<RiderDepositsTab from="a" to="b" />);
    const rows = within(screen.getByRole("table", { name: "Holding cash" })).getAllByRole("row").slice(1);
    expect(within(rows[0]).getByText("Rider")).toBeInTheDocument();
    expect(within(rows[0]).getByText("Held over 24 h")).toBeInTheDocument();
    expect(within(rows[0]).getByText("1 d 2 h")).toBeInTheDocument();
    expect(within(rows[1]).getByText("Bikash Tamang")).toBeInTheDocument();
    expect(within(rows[1]).getByText("Holding")).toBeInTheDocument();
    expect(within(rows[1]).getByText(formatRs(38400))).toBeInTheDocument();
  });

  it("opens Record deposit prefilled — including for a rider whose name is unknown", () => {
    render(<RiderDepositsTab from="a" to="b" />);
    const rows = within(screen.getByRole("table", { name: "Holding cash" })).getAllByRole("row").slice(1);
    fireEvent.click(within(rows[0]).getByRole("button", { name: "Record deposit" }));
    expect(openCreate).toHaveBeenCalledWith({ riderId: "r2", branchId: "b1" });
  });

  it("hides Record deposit without cod.reconcile", () => {
    perms = new Set(["cod.read"]);
    render(<RiderDepositsTab from="a" to="b" />);
    expect(screen.queryByRole("button", { name: "Record deposit" })).toBeNull();
  });

  it("shows recent deposits with server variance and passes the range", () => {
    render(<RiderDepositsTab branchId="b1" from="2026-10-01T00:00:00Z" to="2026-10-07T23:59:59Z" />);
    expect(screen.getByText(`Short − ${formatRs(500)}`)).toBeInTheDocument();
    expect(screen.getByText("Awaiting branch")).toBeInTheDocument();
    expect(riderSettlements.mock.calls[0][0]).toMatchObject({ branchId: "b1", from: "2026-10-01T00:00:00Z", to: "2026-10-07T23:59:59Z" });
    expect(riderCash.mock.calls[0][0]).toEqual({ branchId: "b1" });
  });

  it("shows an empty state instead of zeros when nobody holds cash", () => {
    riderCash.mockReturnValue({ data: { items: [] }, isLoading: false, isError: false });
    render(<RiderDepositsTab from="a" to="b" />);
    expect(screen.getByText("No rider is holding COD cash right now.")).toBeInTheDocument();
  });
});
