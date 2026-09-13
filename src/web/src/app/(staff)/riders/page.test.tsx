import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import RidersPage from "./page";

vi.mock("@/features/riders/hooks/useRiders", () => ({
  useRiders: () => ({ data: [], isLoading: false, isError: false, refetch: vi.fn() }),
}));
vi.mock("@/features/riders/hooks/useVehicles", () => ({
  useVehicles: () => ({ data: [], isLoading: false }),
}));
vi.mock("@/features/riders/hooks/useRidersSummary", () => ({
  useRidersSummary: () => ({ data: { total: 6, active: 5, inactive: 1, currentlyAssigned: 2 }, isLoading: false, isError: false }),
}));
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: () => false }));
vi.mock("@/features/riders/store/riderPanelStore", () => ({
  useRiderPanelStore: (selector: (s: { openCreateRider: () => void; openCreateVehicle: () => void; openAssignTask: () => void }) => unknown) =>
    selector({ openCreateRider: vi.fn(), openCreateVehicle: vi.fn(), openAssignTask: vi.fn() }),
}));

describe("RidersPage", () => {
  it("renders the summary strip above the table using the summary hook's data", async () => {
    render(<RidersPage />);
    await waitFor(() => expect(screen.getByText("Total Riders")).toBeInTheDocument());
    expect(screen.getByText("6")).toBeInTheDocument();
    expect(screen.getByText("Currently Assigned")).toBeInTheDocument();
    expect(screen.getByText("2")).toBeInTheDocument();
  });
});
