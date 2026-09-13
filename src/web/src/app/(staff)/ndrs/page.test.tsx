import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import NdrsPage from "./page";

vi.mock("@/features/ndr/hooks/useNdrs", () => ({
  useNdrs: () => ({ data: { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }, isLoading: false, isError: false, refetch: vi.fn() }),
}));
vi.mock("@/features/ndr/hooks/useNdrsSummary", () => ({
  useNdrsSummary: () => ({ data: { Open: 4, Contacting: 2, CustomerConfirmed: 1, ReattemptScheduled: 1, RtoRequested: 1, Resolved: 6 }, isLoading: false, isError: false }),
}));
vi.mock("@/features/auth/hooks/useSession", () => ({ useSession: () => ({ data: null }) }));
vi.mock("@/features/ndr/store/ndrPanelStore", () => ({
  useNdrPanelStore: (selector: (s: { openDetail: () => void }) => unknown) => selector({ openDetail: vi.fn() }),
}));

describe("NdrsPage", () => {
  it("renders the summary strip above the table using the summary hook's data", async () => {
    render(<NdrsPage />);
    await waitFor(() => expect(screen.getByText("Open")).toBeInTheDocument());
    expect(screen.getByText("Resolved")).toBeInTheDocument();
    expect(screen.getByText("6")).toBeInTheDocument();
  });
});
