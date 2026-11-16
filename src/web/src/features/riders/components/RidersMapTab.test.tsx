import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import type { MapBranchPin, MapRiderPin } from "@/features/rider-tracking/lib/mapPins";
import type { RiderBoardRow } from "@/features/riders/types";
import type { Branch } from "@/features/branches/types";

const mapMounts = vi.hoisted(() => ({ count: 0 }));

vi.mock("next/dynamic", async () => {
  const { useEffect } = await import("react");
  return {
    default: () =>
      function MapStub({ pins, branch }: { pins: MapRiderPin[]; branch?: MapBranchPin }) {
        // Counts mounts so a test can prove the map remounts (and so recentres) per branch.
        useEffect(() => {
          mapMounts.count += 1;
        }, []);
        return (
          <div data-testid="map">
            {branch && <span>{`branch pin ${branch.label}`}</span>}
            {pins.map((p) => (
              <div key={p.riderId} data-testid={`pin-${p.riderId}`} data-stale={String(p.stale)}>
                {p.popup}
              </div>
            ))}
          </div>
        );
      },
  };
});

const useRiderLocations = vi.fn();
vi.mock("@/features/rider-tracking/hooks/useRiderLocations", () => ({
  useRiderLocations: (...args: unknown[]) => useRiderLocations(...args),
}));

import { RidersMapTab } from "./RidersMapTab";

const NOW = new Date("2026-10-16T08:00:00Z");
const minutesAgo = (m: number) => new Date(NOW.getTime() - m * 60_000).toISOString();

function row(overrides: Partial<RiderBoardRow>): RiderBoardRow {
  return {
    riderId: "r1", userId: "u1", fullName: "Asha Gurung", baseBranchId: "b1", roleType: "DeliveryRider", isActive: true,
    vehiclePlate: "BA 2 PA 1111", run: { id: "run1", status: "Active" }, tasksAssigned: 5, tasksCompleted: 2, tasksFailed: 0,
    lastPingAt: minutesAgo(2), ...overrides,
  };
}

const branch = { id: "b1", code: "KTM", name: "Kathmandu Hub", latitude: 27.7, longitude: 85.32 } as Branch;
const onOpenRider = vi.fn();

beforeEach(() => {
  onOpenRider.mockReset();
  useRiderLocations.mockReset();
  useRiderLocations.mockReturnValue({
    data: [
      { riderId: "r1", latitude: 27.71, longitude: 85.31, accuracyMeters: 8.4, recordedAt: minutesAgo(2), receivedAt: minutesAgo(2) },
      { riderId: "r2", latitude: 27.72, longitude: 85.33, accuracyMeters: null, recordedAt: minutesAgo(40), receivedAt: minutesAgo(40) },
    ],
    dataUpdatedAt: NOW.getTime(),
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  });
});

describe("RidersMapTab", () => {
  it("asks for a branch and fetches nothing without one", () => {
    useRiderLocations.mockReturnValue({ data: undefined, isLoading: false, isError: false, refetch: vi.fn() });
    render(<RidersMapTab showCash={false} onOpenRider={onOpenRider} now={NOW} />);
    expect(screen.getByText("Pick a branch to see its riders on the map")).toBeInTheDocument();
    expect(useRiderLocations).toHaveBeenCalledWith(undefined);
  });

  it("shows a skeleton while loading and an error with retry", () => {
    useRiderLocations.mockReturnValue({ data: undefined, isLoading: true, isError: false, refetch: vi.fn() });
    const { unmount } = render(<RidersMapTab branchId="b1" showCash={false} onOpenRider={onOpenRider} now={NOW} />);
    expect(screen.getByRole("status", { name: "Loading rider map" })).toBeInTheDocument();
    unmount();

    useRiderLocations.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn() });
    render(<RidersMapTab branchId="b1" showCash={false} onOpenRider={onOpenRider} now={NOW} />);
    expect(screen.getByText("Couldn't load rider locations")).toBeInTheDocument();
  });

  it("pins riders with board names, marks stale pings and shows the branch pin", () => {
    render(
      <RidersMapTab
        branchId="b1" branch={branch} rows={[row({}), row({ riderId: "r2", fullName: "Bikash Rai", run: null })]}
        showCash={false} onOpenRider={onOpenRider} now={NOW}
      />,
    );
    expect(screen.getByText("branch pin KTM")).toBeInTheDocument();
    expect(screen.getByTestId("pin-r1")).toHaveAttribute("data-stale", "false");
    expect(screen.getByTestId("pin-r2")).toHaveAttribute("data-stale", "true");
    const asha = within(screen.getByTestId("pin-r1"));
    expect(asha.getByText("Asha Gurung")).toBeInTheDocument();
    expect(asha.getByText("Delivery rider · BA 2 PA 1111")).toBeInTheDocument();
    expect(asha.getByText("2 of 5 done")).toBeInTheDocument();
    expect(asha.getByText("Last ping 2 min ago · ±8 m")).toBeInTheDocument();
    expect(asha.queryByText(/Cash held/)).toBeNull();
    expect(within(screen.getByTestId("pin-r2")).getByText("No run today")).toBeInTheDocument();
    expect(within(screen.getByTestId("pin-r2")).getByText("Last ping 40 min ago · stale")).toBeInTheDocument();
    fireEvent.click(asha.getByRole("button", { name: "Open rider" }));
    expect(onOpenRider).toHaveBeenCalledWith("r1");
    expect(screen.getByText(/refreshes every 20 s/)).toBeInTheDocument();
  });

  it("shows cash in the popup only with cash access", () => {
    render(
      <RidersMapTab
        branchId="b1" rows={[row({})]} cash={new Map([["r1", { amount: 2350, parcelCount: 2, oldestCollectedAt: minutesAgo(90) }]])}
        showCash onOpenRider={onOpenRider} now={NOW}
      />,
    );
    expect(within(screen.getByTestId("pin-r1")).getByText("Cash held Rs 2,350")).toBeInTheDocument();
  });

  it("lists on-run riders at this branch that have no position, never on the map", () => {
    render(
      <RidersMapTab
        branchId="b1"
        rows={[
          row({}),
          row({ riderId: "r3", fullName: "Chandra Thapa" }),
          row({ riderId: "r4", fullName: "Off Duty Rider", run: null }),
          row({ riderId: "r5", fullName: "Other Branch", baseBranchId: "b9" }),
        ]}
        showCash={false} onOpenRider={onOpenRider} now={NOW}
      />,
    );
    const list = within(screen.getByRole("region", { name: "On run, not on the map" }));
    expect(list.getByText("Chandra Thapa")).toBeInTheDocument();
    expect(list.queryByText("Off Duty Rider")).toBeNull();
    expect(list.queryByText("Other Branch")).toBeNull();
    expect(screen.queryByTestId("pin-r3")).toBeNull();
    fireEvent.click(list.getByRole("button", { name: "Open Chandra Thapa" }));
    expect(onOpenRider).toHaveBeenCalledWith("r3");
  });

  it("never pins a ping from before today and lists that on-run rider as not on the map", () => {
    useRiderLocations.mockReturnValue({
      data: [
        { riderId: "r1", latitude: 27.71, longitude: 85.31, accuracyMeters: 8.4, recordedAt: minutesAgo(2), receivedAt: minutesAgo(2) },
        {
          riderId: "r3", latitude: 27.72, longitude: 85.33, accuracyMeters: null,
          recordedAt: "2026-10-15T17:00:00Z", receivedAt: "2026-10-15T17:00:00Z",
        },
      ],
      dataUpdatedAt: NOW.getTime(),
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    });
    render(
      <RidersMapTab
        branchId="b1" rows={[row({}), row({ riderId: "r3", fullName: "Chandra Thapa" })]}
        showCash={false} onOpenRider={onOpenRider} now={NOW}
      />,
    );
    expect(screen.getByTestId("pin-r1")).toBeInTheDocument();
    expect(screen.queryByTestId("pin-r3")).toBeNull();
    const list = within(screen.getByRole("region", { name: "On run, not on the map" }));
    expect(list.getByText("Chandra Thapa")).toBeInTheDocument();
  });

  it("remounts the map when the branch changes so it recentres", () => {
    const props = { rows: [row({})], showCash: false, onOpenRider, now: NOW };
    const { rerender } = render(<RidersMapTab branchId="b1" {...props} />);
    const before = mapMounts.count;
    rerender(<RidersMapTab branchId="b1" {...props} />);
    expect(mapMounts.count).toBe(before);
    rerender(<RidersMapTab branchId="b2" {...props} />);
    expect(mapMounts.count).toBe(before + 1);
  });
});
