import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen } from "@testing-library/react";
import { renderWithIntl } from "@/test/renderWithIntl";
import type { RiderBoardRow, RiderToday } from "@/features/riders/types";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";

vi.mock("next/dynamic", () => ({
  default: () => function RouteStub({ points }: { points: unknown[] }) { return <div>{`route map ${points.length} points`}</div>; },
}));
let riderQuery: { data?: unknown; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/riders/hooks/useRider", () => ({ useRider: () => riderQuery }));
let todayQuery: { data?: RiderToday; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/riders/hooks/useRiderToday", () => ({ useRiderToday: () => todayQuery }));
vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b1", code: "KTM", name: "Kathmandu Hub" }] }),
}));
let granted: string[] = [];
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => granted.includes(p) }));
let cashQuery: { data?: unknown; isLoading: boolean; isError: boolean; refetch: () => void };
const useRiderCash = vi.fn();
vi.mock("@/features/cod/hooks/useRiderCash", () => ({ useRiderCash: (...args: unknown[]) => useRiderCash(...args) }));
let crumbs: { data?: unknown[]; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/rider-tracking/hooks/useRunBreadcrumbs", () => ({ useRunBreadcrumbs: () => crumbs }));

import { RiderDetailSheet } from "./RiderDetailSheet";

const ago = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString();

function boardRow(overrides: Partial<RiderBoardRow> = {}): RiderBoardRow {
  return {
    riderId: "r1", userId: "u1", fullName: "Asha Gurung", baseBranchId: "b1", roleType: "DeliveryRider", isActive: true,
    vehiclePlate: "BA 2 PA 1111", run: { id: "run1", status: "Active" }, tasksAssigned: 9, tasksCompleted: 4, tasksFailed: 1,
    lastPingAt: ago(3), ...overrides,
  };
}

function tasks(count: number) {
  return Array.from({ length: count }, (_, i) => ({
    taskId: `t${i + 1}`, shipmentId: `s${i + 1}`, awb: i === 1 ? "" : `AWB-${i + 1}`, type: "Delivery" as const,
    sequence: i + 1, status: i === 0 ? ("Completed" as const) : ("Pending" as const),
  }));
}

beforeEach(() => {
  granted = ["delivery.assign"];
  riderQuery = { data: { id: "r1", userId: "u1", baseBranchId: "b1", licenseNumber: "LIC-7", roleType: "DeliveryRider", isActive: true }, isLoading: false, isError: false, refetch: vi.fn() };
  todayQuery = { data: { rider: boardRow(), tasks: tasks(9) }, isLoading: false, isError: false, refetch: vi.fn() };
  cashQuery = { data: { items: [] }, isLoading: false, isError: false, refetch: vi.fn() };
  useRiderCash.mockReset();
  useRiderCash.mockImplementation(() => cashQuery);
  crumbs = { data: [], isLoading: false, isError: false, refetch: vi.fn() };
  useShipmentPanelStore.setState({ panel: { mode: "closed" } });
  useRiderPanelStore.setState({ panel: { mode: "detail", riderId: "r1" } });
});

describe("RiderDetailSheet", () => {
  it("renders nothing while closed", () => {
    useRiderPanelStore.setState({ panel: { mode: "closed" } });
    renderWithIntl(<RiderDetailSheet />);
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("shows a skeleton while loading and an error with retry", () => {
    todayQuery = { data: undefined, isLoading: true, isError: false, refetch: vi.fn() };
    const { unmount } = renderWithIntl(<RiderDetailSheet />);
    expect(screen.getByText("Loading")).toBeInTheDocument();
    unmount();

    todayQuery = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    renderWithIntl(<RiderDetailSheet />);
    expect(screen.getByText("Couldn't load this rider")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(todayQuery.refetch).toHaveBeenCalled();
  });

  it("shows the header, today's progress and the first eight tasks", () => {
    renderWithIntl(<RiderDetailSheet />);
    expect(screen.getByText("RIDER · DELIVERY · KTM")).toBeInTheDocument();
    expect(screen.getByText("Asha Gurung")).toBeInTheDocument();
    expect(screen.getByText("On run")).toBeInTheDocument();
    expect(screen.getByText("LIC-7")).toBeInTheDocument();
    expect(screen.getByText("BA 2 PA 1111")).toBeInTheDocument();
    expect(screen.getByText("Kathmandu Hub")).toBeInTheDocument();
    expect(screen.getByText("4 of 9 done · 1 failed")).toBeInTheDocument();
    expect(screen.getByText("AWB-8")).toBeInTheDocument();
    expect(screen.queryByText("AWB-9")).toBeNull();
    expect(screen.getByText("+ 1 more tasks")).toBeInTheDocument();
    expect(screen.getByText("Counts are today's run only (date in UTC).")).toBeInTheDocument();
    expect(screen.getByText("Attendance and delivery history")).toBeInTheDocument();
  });

  it("opens the shipment Sheet from an AWB and closes the rider Sheet", () => {
    renderWithIntl(<RiderDetailSheet />);
    fireEvent.click(screen.getByRole("button", { name: "AWB-1" }));
    expect(useShipmentPanelStore.getState().panel).toEqual({ mode: "detail", awb: "AWB-1" });
    expect(useRiderPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("says No run today and offers Assign task only with delivery.assign", () => {
    todayQuery = { data: { rider: boardRow({ run: null, tasksAssigned: 0, tasksCompleted: 0, tasksFailed: 0 }), tasks: [] }, isLoading: false, isError: false, refetch: vi.fn() };
    granted = ["rider.location.read"];
    renderWithIntl(<RiderDetailSheet />);
    expect(screen.getByText("No run today")).toBeInTheDocument();
    expect(screen.getByText("Off duty")).toBeInTheDocument();
    expect(screen.getByText("No location today")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Assign task" })).toBeNull();
  });

  it("hides the location and cash cards without their permissions", () => {
    granted = [];
    renderWithIntl(<RiderDetailSheet />);
    expect(screen.queryByText("Location")).toBeNull();
    expect(screen.queryByText("Cash held")).toBeNull();
    expect(useRiderCash).toHaveBeenCalledWith({}, { enabled: false });
  });

  it("shows today's route and last ping with location access", () => {
    granted = ["rider.location.read"];
    crumbs = {
      data: [
        { riderId: "r1", latitude: 27.7, longitude: 85.3, accuracyMeters: 12, recordedAt: ago(30), receivedAt: ago(30) },
        { riderId: "r1", latitude: 27.71, longitude: 85.31, accuracyMeters: 8.2, recordedAt: ago(3), receivedAt: ago(3) },
      ],
      isLoading: false, isError: false, refetch: vi.fn(),
    };
    renderWithIntl(<RiderDetailSheet />);
    expect(screen.getByText("route map 2 points")).toBeInTheDocument();
    expect(screen.getByText("Last ping 3 min ago · ±8 m · 2 pings today")).toBeInTheDocument();
  });

  it("shows cash held with cod.read, or says none is held", () => {
    granted = ["cod.read"];
    const { unmount } = renderWithIntl(<RiderDetailSheet />);
    expect(screen.getByText("No cash held")).toBeInTheDocument();
    unmount();

    cashQuery = {
      data: { items: [{ riderId: "r1", branchId: "b1", parcelCount: 2, amount: 2350, oldestCollectedAt: ago(90), latestCollectedAt: ago(20) }] },
      isLoading: false, isError: false, refetch: vi.fn(),
    };
    renderWithIntl(<RiderDetailSheet />);
    expect(screen.getByText("Rs 2,350")).toBeInTheDocument();
    expect(screen.getByText("From 2 COD parcels · oldest collected 1 h ago")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open in COD" })).toHaveAttribute("href", "/cod?branch=b1");
  });
});
