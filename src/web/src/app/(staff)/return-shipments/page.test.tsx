import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import type { ReturnShipment } from "@/features/ndr/types";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/return-shipments",
  useSearchParams: () => new URLSearchParams(search),
}));

let listQuery: { data?: ReturnShipment[]; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/ndr/hooks/useReturnShipments", () => ({ useReturnShipments: () => listQuery }));
let summary: { data?: { byRtoStage: Record<string, number>; merchantConfirmed: number; pendingConfirmation: number }; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/ndr/hooks/useReturnShipmentsSummary", () => ({ useReturnShipmentsSummary: () => summary }));
vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b1", code: "KTM", name: "Kathmandu Hub" }, { id: "b2", code: "PKR", name: "Pokhara Hub" }] }),
}));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ branchName: (id: string | null) => (id === "b1" ? "Kathmandu Hub" : "Pokhara Hub"), userName: () => "Staff", riderName: () => "Rider" }),
}));
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: () => true }));
const openDetail = vi.fn();
vi.mock("@/features/ndr/store/returnShipmentPanelStore", () => ({
  useReturnShipmentPanelStore: (selector: (s: unknown) => unknown) => selector({ panel: { mode: "closed" }, openDetail }),
}));

import ReturnShipmentsPage from "./page";

function row(overrides: Partial<ReturnShipment>): ReturnShipment {
  return {
    id: "r1", originalShipmentId: "s1", originalAwb: "KTM-000301", returnAwb: "RTO-20261015-001", rtoReason: "Refused twice",
    originBranchId: "b2", destinationBranchId: "b1", currentStatus: "RtoAtOrigin", currentBranchId: "b1", merchantConfirmedAt: null,
    ...overrides,
  };
}

beforeEach(() => {
  replace.mockReset();
  openDetail.mockReset();
  search = "";
  summary = { data: { byRtoStage: { RtoInTransit: 2, RtoAtOrigin: 3, RtoDelivered: 4 }, merchantConfirmed: 4, pendingConfirmation: 5 }, isLoading: false, isError: false, refetch: vi.fn() };
  listQuery = {
    data: [row({}), row({ id: "r2", returnAwb: "RTO-20261015-002", currentStatus: "RtoInTransit", currentBranchId: null })],
    isLoading: false, isError: false, refetch: vi.fn(),
  };
});

describe("ReturnShipmentsPage", () => {
  it("shows the five-stage strip with At origin highlighted and the NDR/Returns switch", () => {
    render(<ReturnShipmentsPage />);
    expect(screen.getByRole("heading", { name: "Returns to origin" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Returns" })).toHaveAttribute("aria-selected", "true");
    const strip = screen.getByRole("list", { name: "Return stages" });
    expect(strip.querySelectorAll("li")).toHaveLength(5);
    expect(screen.getByRole("button", { name: /^At origin/ })).toHaveAttribute("aria-current", "step");
    expect(screen.getByRole("button", { name: /^Pending confirmation/ })).toHaveTextContent("5");
  });

  it("filters rows by the stage in the URL and writes stage clicks back", () => {
    search = "stage=RtoInTransit";
    render(<ReturnShipmentsPage />);
    const table = screen.getByRole("table");
    expect(within(table).getByText("RTO-20261015-002")).toBeInTheDocument();
    expect(within(table).queryByText("RTO-20261015-001")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: /^At origin/ }));
    expect(replace).toHaveBeenLastCalledWith("/return-shipments?stage=RtoAtOrigin", { scroll: false });
  });

  it("links each return to its original, routes by code and falls back to 'In transit'", () => {
    render(<ReturnShipmentsPage />);
    const table = screen.getByRole("table");
    expect(within(table).getAllByText("from KTM-000301")).toHaveLength(2);
    expect(within(table).getAllByText("PKR → KTM")).toHaveLength(2);
    expect(within(table).getByText("In transit")).toBeInTheDocument();
    expect(within(table).getAllByText("Not confirmed")).toHaveLength(2);
    fireEvent.click(within(table).getByRole("button", { name: /RTO-20261015-001/ }));
    expect(openDetail).toHaveBeenCalledWith("r1");
  });

  it("never shows zeros while loading, and shows an error when the list fails", () => {
    summary = { data: undefined, isLoading: true, isError: false, refetch: vi.fn() };
    listQuery = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    render(<ReturnShipmentsPage />);
    expect(screen.getByRole("status", { name: "Loading return stages" })).toBeInTheDocument();
    expect(screen.getByText("Couldn't load returns")).toBeInTheDocument();
  });
});
