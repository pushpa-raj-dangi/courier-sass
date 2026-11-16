import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, within } from "@testing-library/react";
import { renderWithIntl } from "@/test/renderWithIntl";
import type { Branch, BranchActivity } from "@/features/branches/types";
import { useBranchPanelStore } from "@/features/branches/store/branchPanelStore";

vi.mock("next/dynamic", () => ({
  default: () =>
    function MapStub({ pins, branch }: { pins: unknown[]; branch?: { label: string } }) {
      return <div>{`mini map ${branch?.label ?? ""} with ${pins.length} riders`}</div>;
    },
}));

const BRANCH: Branch = {
  id: "b1", code: "KTM", name: "Kathmandu Hub", type: "Hub", provinceId: "p1", districtId: "d1", localLevelId: "l1",
  wardNumber: 5, addressLine: "New Road", phone: "01-4444444", operatingHours: "9–6", latitude: 27.7, longitude: 85.32,
  parentBranchId: "b0", deliveryZoneId: "z1", isActive: true,
};

let branchQuery: { data?: Branch; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/branches/hooks/useBranch", () => ({ useBranch: () => branchQuery }));
vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b0", code: "HQ", name: "Head Office" }, BRANCH] }),
}));
let activity: { data?: BranchActivity; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/branches/hooks/useBranchActivity", () => ({ useBranchActivity: () => activity }));
let granted: string[] = [];
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => granted.includes(p) }));
const useBranchDashboard = vi.fn();
vi.mock("@/features/reports/hooks/useBranchDashboard", () => ({ useBranchDashboard: (...a: unknown[]) => useBranchDashboard(...a) }));
vi.mock("@/features/geography/hooks/useGeography", () => ({
  useDistricts: () => ({ data: [{ id: "d1", provinceId: "p1", name: "Kathmandu" }] }),
  useLocalLevels: () => ({ data: [{ id: "l1", districtId: "d1", name: "Kathmandu Metropolitan City" }] }),
}));
vi.mock("@/features/geography/hooks/useDeliveryZones", () => ({ useDeliveryZones: () => ({ data: [{ id: "z1", name: "Valley" }] }) }));
vi.mock("@/features/branches/hooks/useUpdateDeliveryZone", () => ({
  useUpdateDeliveryZone: () => ({ mutate: vi.fn(), isPending: false, isError: false }),
}));
const useRiderLocations = vi.fn();
vi.mock("@/features/rider-tracking/hooks/useRiderLocations", () => ({ useRiderLocations: (...a: unknown[]) => useRiderLocations(...a) }));
vi.mock("@/features/riders/hooks/useRiderBoard", () => ({ useRiderBoard: () => ({ data: { items: [] } }) }));

import { BranchDetailSheet } from "./BranchDetailSheet";

beforeEach(() => {
  granted = [];
  branchQuery = { data: BRANCH, isLoading: false, isError: false, refetch: vi.fn() };
  activity = { data: { items: [{ branchId: "b1", onHand: 12, outForDelivery: 4, ridersOut: 3, openExceptions: 2 }] }, isLoading: false, isError: false, refetch: vi.fn() };
  useBranchDashboard.mockReset();
  useBranchDashboard.mockReturnValue({ data: undefined, isLoading: false, isError: false, refetch: vi.fn() });
  useRiderLocations.mockReset();
  useRiderLocations.mockReturnValue({
    data: [{ riderId: "r1", latitude: 27.71, longitude: 85.31, accuracyMeters: 5, recordedAt: new Date().toISOString(), receivedAt: new Date().toISOString() }],
  });
  useBranchPanelStore.setState({ panel: { mode: "detail", branchId: "b1" } });
});

describe("BranchDetailSheet", () => {
  it("shows the header with type, parent, code, status and facts", () => {
    renderWithIntl(<BranchDetailSheet />);
    expect(screen.getByText("BRANCH · HUB · PARENT HQ")).toBeInTheDocument();
    expect(screen.getByText("Kathmandu Hub")).toBeInTheDocument();
    expect(screen.getAllByText("KTM").length).toBeGreaterThan(0);
    expect(screen.getByText("Active")).toBeInTheDocument();
    expect(screen.getAllByText("New Road, Ward 5, Kathmandu Metropolitan City, Kathmandu")).toHaveLength(2);
    expect(screen.getByText("01-4444444")).toBeInTheDocument();
    expect(screen.getByText("Valley")).toBeInTheDocument();
    expect(screen.getByText("27.70000, 85.32000")).toBeInTheDocument();
    expect(screen.getByText("Edit details, staff roster, weekly hours")).toBeInTheDocument();
  });

  it("links the right-now cards to filtered lists, never claiming a list length", () => {
    renderWithIntl(<BranchDetailSheet />);
    const cards = within(screen.getByRole("region", { name: "Right now" }));
    expect(cards.getByRole("link", { name: /On hands*12/ })).toHaveAttribute(
      "href",
      "/shipments?branchId=b1&status=AtOriginBranch&status=Sorted&status=AtDestinationBranch&status=RtoInitiated&status=RtoAtOrigin",
    );
    expect(cards.getByRole("link", { name: /Out for deliverys*4/ })).toHaveAttribute("href", "/shipments?branchId=b1&status=OutForDelivery");
    expect(cards.getByRole("link", { name: /Riders outs*3/ })).toHaveAttribute("href", "/riders?branch=b1&stage=on");
    expect(cards.getByRole("link", { name: /Open exceptionss*2/ })).toHaveAttribute("href", "/ndrs");
    expect(cards.getAllByText("View shipments")).toHaveLength(2);
  });

  it("shows dashes while activity loads and zeros for a branch missing from loaded activity", () => {
    activity = { data: undefined, isLoading: true, isError: false, refetch: vi.fn() };
    const { unmount } = renderWithIntl(<BranchDetailSheet />);
    expect(within(screen.getByRole("region", { name: "Right now" })).getAllByText("—")).toHaveLength(4);
    unmount();

    activity = { data: { items: [] }, isLoading: false, isError: false, refetch: vi.fn() };
    renderWithIntl(<BranchDetailSheet />);
    expect(within(screen.getByRole("region", { name: "Right now" })).getAllByText("0")).toHaveLength(4);
  });

  it("shows the location map only with rider.location.read and coordinates", () => {
    const { unmount } = renderWithIntl(<BranchDetailSheet />);
    expect(screen.queryByText(/mini map/)).toBeNull();
    expect(useRiderLocations).toHaveBeenLastCalledWith(undefined);
    unmount();

    granted = ["rider.location.read"];
    renderWithIntl(<BranchDetailSheet />);
    expect(screen.getByText("mini map KTM with 1 riders")).toBeInTheDocument();
    expect(screen.getAllByText("New Road, Ward 5, Kathmandu Metropolitan City, Kathmandu")).toHaveLength(1);
    expect(useRiderLocations).toHaveBeenLastCalledWith("b1");
  });

  it("leaves pings from before today off the location map", () => {
    granted = ["rider.location.read"];
    const today = new Date().toISOString();
    const twoDaysAgo = new Date(Date.now() - 2 * 24 * 60 * 60_000).toISOString();
    useRiderLocations.mockReturnValue({
      data: [
        { riderId: "r1", latitude: 27.71, longitude: 85.31, accuracyMeters: 5, recordedAt: today, receivedAt: today },
        { riderId: "r2", latitude: 27.72, longitude: 85.32, accuracyMeters: 5, recordedAt: twoDaysAgo, receivedAt: twoDaysAgo },
      ],
      isLoading: false,
      isError: false,
    });
    renderWithIntl(<BranchDetailSheet />);
    expect(screen.getByText("mini map KTM with 1 riders")).toBeInTheDocument();
  });

  it("loads the shipments and exceptions sections only with report.read", () => {
    const { unmount } = renderWithIntl(<BranchDetailSheet />);
    expect(useBranchDashboard).toHaveBeenLastCalledWith("");
    unmount();

    granted = ["report.read"];
    renderWithIntl(<BranchDetailSheet />);
    expect(useBranchDashboard).toHaveBeenLastCalledWith("b1");
  });

  it("shows an error state when the branch cannot be read", () => {
    branchQuery = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    renderWithIntl(<BranchDetailSheet />);
    expect(screen.getByText("Couldn't load this branch")).toBeInTheDocument();
  });
});
