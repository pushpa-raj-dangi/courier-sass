import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import LiveMapPage from "./page";

const mockUseRiderLocations = vi.fn();
let mockActiveBranchId: string | null = "branch-1";

vi.mock("@/lib/store/uiStore", () => ({
  useUiStore: (selector: (s: { activeBranchId: string | null }) => unknown) => selector({ activeBranchId: mockActiveBranchId }),
}));
vi.mock("@/features/riders/hooks/useRiders", () => ({
  useRiders: () => ({ data: [] }),
}));
vi.mock("@/features/rider-tracking/hooks/useRiderLocations", () => ({
  useRiderLocations: (...args: unknown[]) => mockUseRiderLocations(...args),
}));

describe("LiveMapPage", () => {
  it("renders the 'select a branch' empty state when no active branch is selected", () => {
    mockActiveBranchId = null;
    mockUseRiderLocations.mockReturnValue({ data: undefined, isLoading: false, isError: false, refetch: vi.fn() });

    render(<LiveMapPage />);

    expect(screen.getByText("Select a branch")).toBeInTheDocument();
  });

  it("renders the error state when useRiderLocations errors", () => {
    mockActiveBranchId = "branch-1";
    mockUseRiderLocations.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn() });

    render(<LiveMapPage />);

    expect(screen.getByText("Couldn't load rider locations")).toBeInTheDocument();
  });

  it("renders the loading state while fetching", () => {
    mockActiveBranchId = "branch-1";
    mockUseRiderLocations.mockReturnValue({ data: undefined, isLoading: true, isError: false, refetch: vi.fn() });

    render(<LiveMapPage />);

    expect(screen.getByRole("status")).toBeInTheDocument();
  });

  it("renders the 'no active riders' empty state when the branch has no reported positions", () => {
    mockActiveBranchId = "branch-1";
    mockUseRiderLocations.mockReturnValue({ data: [], isLoading: false, isError: false, refetch: vi.fn() });

    render(<LiveMapPage />);

    expect(screen.getByText("No active riders")).toBeInTheDocument();
  });
});
