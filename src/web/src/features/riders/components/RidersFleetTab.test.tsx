import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import type { RiderBoardRow, Vehicle } from "@/features/riders/types";
import { RidersFleetTab } from "./RidersFleetTab";

const vehicles: Vehicle[] = [
  { id: "v1", riderId: null, type: "Bike", plateNumber: "BA 1 PA 0001", capacity: null },
  { id: "v2", riderId: "r1", type: "Van", plateNumber: "BA 2 KHA 1234", capacity: 500 },
  { id: "v3", riderId: "gone", type: "Bike", plateNumber: "BA 3 PA 0003", capacity: 20 },
];
const rows: RiderBoardRow[] = [
  {
    riderId: "r1", userId: "u1", fullName: "Asha Gurung", baseBranchId: "b1", roleType: "DeliveryRider", isActive: true,
    vehiclePlate: "BA 2 KHA 1234", run: { id: "run1", status: "Active" }, tasksAssigned: 6, tasksCompleted: 4, tasksFailed: 1, lastPingAt: null,
  },
];

describe("RidersFleetTab", () => {
  it("lists vehicles with capacity, rider and the rider's day", () => {
    render(<RidersFleetTab vehicles={vehicles} isLoading={false} isError={false} onRetry={vi.fn()} rows={rows} filter="all" onFilterChange={vi.fn()} />);
    expect(screen.getByText("BA 2 KHA 1234")).toBeInTheDocument();
    expect(screen.getByText("500 kg")).toBeInTheDocument();
    expect(screen.getByText("Asha Gurung")).toBeInTheDocument();
    expect(screen.getByText("On run")).toBeInTheDocument();
    expect(screen.getByText("4 of 6")).toBeInTheDocument();
    expect(screen.getByText("Unassigned")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "All 3" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByText("Reassigning a vehicle or marking it off the road needs backend support.")).toBeInTheDocument();
  });

  it("filters to unassigned vehicles and reports chip clicks", () => {
    const onFilterChange = vi.fn();
    render(<RidersFleetTab vehicles={vehicles} isLoading={false} isError={false} onRetry={vi.fn()} rows={rows} filter="unassigned" onFilterChange={onFilterChange} />);
    expect(screen.getByText("BA 1 PA 0001")).toBeInTheDocument();
    expect(screen.queryByText("BA 2 KHA 1234")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "All 3" }));
    expect(onFilterChange).toHaveBeenCalledWith("all");
    expect(screen.getByRole("button", { name: "Unassigned 1" })).toHaveAttribute("aria-pressed", "true");
  });

  it("shows an error with retry", () => {
    const onRetry = vi.fn();
    render(<RidersFleetTab vehicles={undefined} isLoading={false} isError onRetry={onRetry} rows={rows} filter="all" onFilterChange={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: /retry/i }));
    expect(onRetry).toHaveBeenCalled();
  });
});
