import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import ManifestsPage from "./page";

vi.mock("@/features/manifests/hooks/useManifests", () => ({
  useManifests: () => ({ data: [], isLoading: false, isError: false, refetch: vi.fn() }),
}));
vi.mock("@/features/manifests/hooks/useManifestsSummary", () => ({
  useManifestsSummary: () => ({ data: { Draft: 1, Sealed: 2, InTransit: 3, Received: 1, Closed: 4 }, isLoading: false, isError: false }),
}));
vi.mock("@/features/branches/hooks/useBranches", () => ({ useBranches: () => ({ data: [] }) }));
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: () => false }));
vi.mock("@/lib/store/uiStore", () => ({ useUiStore: (selector: (s: { activeBranchId: string | null }) => unknown) => selector({ activeBranchId: null }) }));
vi.mock("@/features/manifests/store/manifestPanelStore", () => ({
  useManifestPanelStore: (selector: (s: { openCreate: () => void; openDetail: () => void }) => unknown) =>
    selector({ openCreate: vi.fn(), openDetail: vi.fn() }),
}));

describe("ManifestsPage", () => {
  it("renders the summary strip above the table using the summary hook's data", async () => {
    render(<ManifestsPage />);
    await waitFor(() => expect(screen.getByText("Draft")).toBeInTheDocument());
    expect(screen.getByText("Closed")).toBeInTheDocument();
    expect(screen.getByText("4")).toBeInTheDocument();
  });
});
