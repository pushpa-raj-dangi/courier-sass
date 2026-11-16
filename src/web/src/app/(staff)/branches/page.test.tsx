import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import type { Branch, BranchActivity } from "@/features/branches/types";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/branches",
  useSearchParams: () => new URLSearchParams(search),
}));

function branch(overrides: Partial<Branch>): Branch {
  return {
    id: "b1", code: "KTM", name: "Kathmandu Hub", type: "Hub", provinceId: "p1", districtId: "d1", localLevelId: "l1",
    wardNumber: 1, addressLine: "New Road", phone: null, operatingHours: null, latitude: null, longitude: null,
    parentBranchId: null, deliveryZoneId: null, isActive: true, ...overrides,
  };
}

let list: { data?: Branch[]; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/branches/hooks/useBranches", () => ({ useBranches: () => list }));
let summary: { data?: unknown; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/branches/hooks/useBranchesSummary", () => ({ useBranchesSummary: () => summary }));
let activity: { data?: BranchActivity; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/branches/hooks/useBranchActivity", () => ({ useBranchActivity: () => activity }));
vi.mock("@/features/geography/hooks/useDistrictNames", () => ({
  useDistrictNames: () => new Map([["d1", "Kathmandu"], ["d2", "Kaski"]]),
}));
let granted: string[] = [];
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => granted.includes(p) }));
const openDetail = vi.fn();
const openCreate = vi.fn();
vi.mock("@/features/branches/store/branchPanelStore", () => ({
  useBranchPanelStore: (selector: (s: unknown) => unknown) => selector({ panel: { mode: "closed" }, openCreate, openDetail }),
}));

import BranchesPage from "./page";

beforeEach(() => {
  replace.mockReset();
  openDetail.mockReset();
  search = "";
  granted = ["branch.create"];
  list = {
    data: [
      branch({}),
      branch({ id: "b2", code: "PKR", name: "Pokhara Branch", type: "Branch", provinceId: "p2", districtId: "d2" }),
      branch({ id: "b3", code: "OLD", name: "Old Depot", type: "Warehouse", isActive: false }),
    ],
    isLoading: false, isError: false, refetch: vi.fn(),
  };
  summary = {
    data: { total: 3, active: 2, inactive: 1, byType: { HeadOffice: 0, Hub: 1, Branch: 1, PickupPoint: 0, Warehouse: 1 } },
    isLoading: false, isError: false, refetch: vi.fn(),
  };
  activity = {
    data: { items: [{ branchId: "b1", onHand: 12, outForDelivery: 4, ridersOut: 3, openExceptions: 2 }] },
    isLoading: false, isError: false, refetch: vi.fn(),
  };
});

function rowFor(name: RegExp) {
  return screen.getByRole("button", { name }).closest("tr")!;
}

describe("BranchesPage", () => {
  it("shows the subtitle and the five-type strip from the summary", () => {
    render(<BranchesPage />);
    expect(screen.getByText("2 active · 1 inactive")).toBeInTheDocument();
    const strip = screen.getByRole("list", { name: "Branch types" });
    expect(within(strip).getByRole("button", { name: /Hub/ })).toHaveTextContent("1");
    expect(within(strip).getAllByRole("button")).toHaveLength(5);
  });

  it("filters by type from the strip", () => {
    const { unmount } = render(<BranchesPage />);
    fireEvent.click(within(screen.getByRole("list", { name: "Branch types" })).getByRole("button", { name: /^Branch/ }));
    expect(replace).toHaveBeenCalledWith("/branches?type=Branch", { scroll: false });
    unmount();

    search = "type=Branch";
    render(<BranchesPage />);
    expect(screen.getByRole("button", { name: /Pokhara Branch/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Kathmandu Hub/ })).toBeNull();
  });

  it("searches by code and shows inactive branches only when asked", () => {
    search = "q=pkr";
    const { unmount } = render(<BranchesPage />);
    expect(screen.queryByRole("button", { name: /Kathmandu Hub/ })).toBeNull();
    expect(screen.getByRole("button", { name: /Pokhara Branch/ })).toBeInTheDocument();
    unmount();

    search = "";
    const second = render(<BranchesPage />);
    expect(screen.queryByRole("button", { name: /Old Depot/ })).toBeNull();
    fireEvent.click(screen.getByRole("checkbox", { name: "Active only" }));
    expect(replace).toHaveBeenCalledWith("/branches?inactive=1", { scroll: false });
    second.unmount();

    search = "inactive=1";
    render(<BranchesPage />);
    expect(screen.getByRole("button", { name: /Old Depot/ })).toBeInTheDocument();
  });

  it("shows live counts, zeros for quiet branches, district names and worded exceptions", () => {
    render(<BranchesPage />);
    const ktm = within(rowFor(/Kathmandu Hub/));
    expect(ktm.getByText("12")).toBeInTheDocument();
    expect(ktm.getByText("4")).toBeInTheDocument();
    expect(ktm.getByText("3")).toBeInTheDocument();
    expect(ktm.getByText("2 open")).toBeInTheDocument();
    expect(ktm.getByText("Kathmandu")).toBeInTheDocument();
    const pkr = within(rowFor(/Pokhara Branch/));
    expect(pkr.getByText("None")).toBeInTheDocument();
    expect(pkr.getAllByText("0")).toHaveLength(3);
    expect(pkr.getByText("Kaski")).toBeInTheDocument();
  });

  it("shows dashes, never zeros, while activity loads, and a retry when it fails", () => {
    activity = { data: undefined, isLoading: true, isError: false, refetch: vi.fn() };
    const { unmount } = render(<BranchesPage />);
    const loading = within(rowFor(/Kathmandu Hub/));
    expect(loading.getAllByText("—")).toHaveLength(4);
    expect(loading.queryByText("0")).toBeNull();
    unmount();

    activity = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    render(<BranchesPage />);
    expect(screen.getByText("Couldn't load live counts")).toBeInTheDocument();
    expect(within(rowFor(/Kathmandu Hub/)).getAllByText("—")).toHaveLength(4);
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(activity.refetch).toHaveBeenCalled();
  });

  it("opens the Sheet from a row and gates Add branch", () => {
    const { unmount } = render(<BranchesPage />);
    fireEvent.click(screen.getByRole("button", { name: /Kathmandu Hub/ }));
    expect(openDetail).toHaveBeenCalledWith("b1");
    fireEvent.click(screen.getByRole("button", { name: /Add branch/ }));
    expect(openCreate).toHaveBeenCalled();
    unmount();

    granted = [];
    render(<BranchesPage />);
    expect(screen.queryByRole("button", { name: /Add branch/ })).toBeNull();
  });

  it("never shows zeros in the strip or subtitle while the summary loads", () => {
    summary = { data: undefined, isLoading: true, isError: false, refetch: vi.fn() };
    render(<BranchesPage />);
    expect(screen.getByRole("status", { name: "Loading branch types" })).toBeInTheDocument();
    expect(screen.getByText("—")).toBeInTheDocument();
  });
});
