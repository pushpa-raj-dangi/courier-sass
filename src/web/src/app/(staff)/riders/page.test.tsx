import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import type { RiderBoardRow } from "@/features/riders/types";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/riders",
  useSearchParams: () => new URLSearchParams(search),
}));

let board: { data?: { items: RiderBoardRow[] }; isLoading: boolean; isError: boolean; refetch: () => void };
const useRiderBoard = vi.fn();
vi.mock("@/features/riders/hooks/useRiderBoard", () => ({ useRiderBoard: (...args: unknown[]) => useRiderBoard(...args) }));
const useRiderCash = vi.fn();
vi.mock("@/features/cod/hooks/useRiderCash", () => ({ useRiderCash: (...args: unknown[]) => useRiderCash(...args) }));
vi.mock("@/features/riders/hooks/useVehicles", () => ({
  useVehicles: () => ({ data: [{ id: "v1", riderId: null, type: "Bike", plateNumber: "BA 1", capacity: null }], isLoading: false, isError: false, refetch: vi.fn() }),
}));
let session: { hasAllBranchesScope: boolean; branches: { branchId: string }[] };
vi.mock("@/features/auth/hooks/useSession", () => ({ useSession: () => ({ data: { userId: "u1", ...session } }) }));
vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b1", code: "KTM", name: "Kathmandu Hub" }, { id: "b2", code: "PKR", name: "Pokhara Hub" }] }),
}));
let granted: string[] = [];
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => granted.includes(p) }));
const openDetail = vi.fn();
vi.mock("@/features/riders/store/riderPanelStore", () => ({
  useRiderPanelStore: (selector: (s: unknown) => unknown) =>
    selector({ panel: { mode: "closed" }, openDetail, openCreateRider: vi.fn(), openCreateVehicle: vi.fn(), openAssignTask: vi.fn() }),
}));
vi.mock("@/features/riders/components/RidersMapTab", () => ({
  RidersMapTab: ({ branchId }: { branchId?: string }) => <div data-testid="map-tab">{`map for ${branchId ?? "none"}`}</div>,
}));
vi.mock("@/features/riders/components/RidersFleetTab", () => ({
  RidersFleetTab: ({ vehicles }: { vehicles?: unknown[] }) => <div data-testid="fleet-tab">{`fleet ${vehicles?.length ?? "—"}`}</div>,
}));

import RidersPage from "./page";

const ago = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString();

function row(overrides: Partial<RiderBoardRow>): RiderBoardRow {
  return {
    riderId: "r1", userId: "u1", fullName: "Asha Gurung", baseBranchId: "b1", roleType: "DeliveryRider", isActive: true,
    vehiclePlate: "BA 2 PA 1111", run: { id: "run1", status: "Active" }, tasksAssigned: 6, tasksCompleted: 4, tasksFailed: 1,
    lastPingAt: ago(3), ...overrides,
  };
}

const ROWS = [
  row({}),
  row({ riderId: "r2", fullName: "Bikash Rai", tasksAssigned: 3, tasksCompleted: 1, tasksFailed: 0, lastPingAt: ago(40), vehiclePlate: null }),
  row({ riderId: "r3", fullName: "Chandra Thapa", roleType: "PickupRider", run: null, tasksAssigned: 0, tasksCompleted: 0, tasksFailed: 0, lastPingAt: null }),
  row({ riderId: "r4", fullName: "Dipa Karki", run: { id: "run4", status: "Active" }, tasksAssigned: 0, tasksCompleted: 0, tasksFailed: 0, lastPingAt: ago(5) }),
  row({ riderId: "r5", fullName: "Old Rider", isActive: false, run: null }),
];

beforeEach(() => {
  replace.mockReset();
  openDetail.mockReset();
  search = "";
  granted = ["rider.location.read", "cod.read", "delivery.assign", "rider.create"];
  session = { hasAllBranchesScope: true, branches: [] };
  board = { data: { items: ROWS }, isLoading: false, isError: false, refetch: vi.fn() };
  useRiderBoard.mockReset();
  useRiderBoard.mockImplementation(() => board);
  useRiderCash.mockReset();
  useRiderCash.mockReturnValue({
    data: { items: [{ riderId: "r1", branchId: "b1", parcelCount: 2, amount: 2350, oldestCollectedAt: ago(60), latestCollectedAt: ago(10) }] },
    isLoading: false, isError: false, refetch: vi.fn(),
  });
});

describe("RidersPage", () => {
  it("counts active riders by stage and highlights stale on-run locations", () => {
    render(<RidersPage />);
    const strip = screen.getByRole("list", { name: "Rider day" });
    expect(within(strip).getByRole("button", { name: /Off duty/ })).toHaveTextContent("1");
    const onRun = within(strip).getByRole("button", { name: /On run/ });
    expect(onRun).toHaveTextContent("3");
    expect(onRun).toHaveAttribute("aria-current", "step");
    expect(onRun).toHaveTextContent("1 with stale location");
    expect(screen.getByText("All branches · 3 out today")).toBeInTheDocument();
  });

  it("never shows zeros while the board loads or fails", () => {
    board = { data: undefined, isLoading: true, isError: false, refetch: vi.fn() };
    const { unmount } = render(<RidersPage />);
    expect(screen.getByRole("status", { name: "Loading rider counts" })).toBeInTheDocument();
    expect(screen.getByText("All branches · —")).toBeInTheDocument();
    unmount();

    board = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    render(<RidersPage />);
    expect(screen.getByText("Couldn't load rider counts")).toBeInTheDocument();
  });

  it("renders the rider table with progress, last seen, cash and status", () => {
    render(<RidersPage />);
    expect(screen.getByRole("button", { name: /Asha Gurung/ })).toBeInTheDocument();
    expect(screen.getByText("4 of 6")).toBeInTheDocument();
    expect(screen.getByText("1 failed")).toBeInTheDocument();
    expect(screen.getByText("0 of 0")).toBeInTheDocument();
    expect(screen.getByText("No run")).toBeInTheDocument();
    expect(screen.getByText("3 min ago")).toBeInTheDocument();
    expect(screen.getByText("40 min ago · stale")).toBeInTheDocument();
    expect(screen.getByText("No ping today")).toBeInTheDocument();
    expect(screen.getByText("Unassigned")).toBeInTheDocument();
    expect(screen.getByText("Rs 2,350")).toBeInTheDocument();
    expect(screen.queryByText("Old Rider")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: /Asha Gurung/ }));
    expect(openDetail).toHaveBeenCalledWith("r1");
  });

  it("hides the cash column without cod.read and the Map tab without rider.location.read", () => {
    granted = [];
    render(<RidersPage />);
    expect(screen.queryByText("Cash held")).toBeNull();
    expect(screen.queryByRole("tab", { name: "Map" })).toBeNull();
    expect(useRiderCash).toHaveBeenCalledWith({ branchId: undefined }, { enabled: false });
    expect(screen.queryByRole("button", { name: /Add rider/ })).toBeNull();
  });

  it("filters by stage from the strip and clears it from the chip", () => {
    const { unmount } = render(<RidersPage />);
    fireEvent.click(screen.getByRole("button", { name: /Off duty/ }));
    expect(replace).toHaveBeenCalledWith("/riders?stage=off", { scroll: false });
    unmount();

    search = "stage=off";
    render(<RidersPage />);
    expect(screen.getByRole("button", { name: /Chandra Thapa/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Asha Gurung/ })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Clear stage filter Off duty" }));
    expect(replace).toHaveBeenLastCalledWith("/riders", { scroll: false });
  });

  it("shows inactive riders and filters by role from the URL", () => {
    search = "inactive=1&role=Delivery";
    render(<RidersPage />);
    expect(screen.getByText("Old Rider")).toBeInTheDocument();
    expect(screen.queryByText("Chandra Thapa")).toBeNull();
    expect(screen.getByText("Inactive")).toBeInTheDocument();
  });

  it("switches tabs through the URL and passes the picked branch", () => {
    render(<RidersPage />);
    fireEvent.mouseDown(screen.getByRole("tab", { name: /Fleet/ }));
    expect(replace).toHaveBeenCalledWith("/riders?tab=fleet", { scroll: false });

    search = "tab=map&branch=b2";
    render(<RidersPage />);
    expect(screen.getByText("map for b2")).toBeInTheDocument();
    expect(useRiderBoard).toHaveBeenLastCalledWith("b2");
  });

  it("gives a one-branch user's map their branch without a picker", () => {
    session = { hasAllBranchesScope: false, branches: [{ branchId: "b1" }] };
    search = "tab=map";
    render(<RidersPage />);
    expect(screen.getByText("map for b1")).toBeInTheDocument();
    expect(screen.queryByRole("combobox", { name: "Branch" })).toBeNull();
  });

  it("falls back to the Riders tab when the Map tab is not allowed", () => {
    granted = [];
    search = "tab=map";
    render(<RidersPage />);
    expect(screen.queryByTestId("map-tab")).toBeNull();
    expect(screen.getByRole("button", { name: /Asha Gurung/ })).toBeInTheDocument();
  });
});
