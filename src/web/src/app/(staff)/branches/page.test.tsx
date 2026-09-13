import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import BranchesPage from "./page";

vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [], isLoading: false, isError: false, refetch: vi.fn() }),
}));
vi.mock("@/features/branches/hooks/useBranchesSummary", () => ({
  useBranchesSummary: () => ({ data: { total: 5, active: 4, inactive: 1, byType: { Hub: 2 } }, isLoading: false, isError: false }),
}));
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: () => false }));
vi.mock("@/features/branches/store/branchPanelStore", () => ({
  useBranchPanelStore: (selector: (s: { openCreate: () => void; openDetail: () => void }) => unknown) =>
    selector({ openCreate: vi.fn(), openDetail: vi.fn() }),
}));

describe("BranchesPage", () => {
  it("renders the summary strip above the table using the summary hook's data", async () => {
    render(<BranchesPage />);
    await waitFor(() => expect(screen.getByText("Total Branches")).toBeInTheDocument());
    expect(screen.getByText("5")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
    expect(screen.getByText("Inactive")).toBeInTheDocument();
  });
});
