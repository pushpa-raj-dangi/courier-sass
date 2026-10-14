import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderWithIntl } from "@/test/renderWithIntl";
import { formatRs } from "@/features/cod/lib/codFormat";

vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b1", name: "Kathmandu Hub" }, { id: "b2", name: "Pokhara Hub" }, { id: "b3", name: "Butwal Branch" }] }),
}));
let session: unknown;
vi.mock("@/features/auth/hooks/useSession", () => ({ useSession: () => ({ data: session }) }));
vi.mock("@/features/riders/hooks/useRiders", () => ({
  useRiders: () => ({
    data: [
      { id: "r1", userId: "u1", baseBranchId: "b1", licenseNumber: "LIC-001", roleType: "DeliveryRider", isActive: true },
      // Based at Pokhara but holding cash collected at Kathmandu.
      { id: "r2", userId: "u2", baseBranchId: "b2", licenseNumber: "LIC-002", roleType: "DeliveryRider", isActive: true },
    ],
  }),
}));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ riderName: () => "Rider", branchName: () => "Kathmandu Hub", userName: () => "Staff" }),
}));
const holdings = [
  { riderId: "r1", branchId: "b1", parcelCount: 3, amount: 2500, oldestCollectedAt: "2026-10-07T09:00:00Z", latestCollectedAt: "2026-10-07T10:00:00Z" },
  { riderId: "r2", branchId: "b1", parcelCount: 1, amount: 1200, oldestCollectedAt: "2026-10-07T08:00:00Z", latestCollectedAt: "2026-10-07T08:00:00Z" },
];
vi.mock("@/features/cod/hooks/useRiderCash", () => ({
  useRiderCash: (params: { branchId?: string }) => ({
    data: { items: holdings.filter((h) => !params.branchId || h.branchId === params.branchId) },
    isLoading: false,
    isPending: false,
  }),
}));
const mutateAsync = vi.fn();
vi.mock("@/features/cod/hooks/useCreateRiderSettlement", () => ({ useCreateRiderSettlement: () => ({ mutateAsync, isPending: false }) }));
const close = vi.fn();
let panel: unknown = { mode: "rider-settlement-create" };
vi.mock("@/features/cod/store/codPanelStore", () => ({
  useCodPanelStore: (selector: (s: unknown) => unknown) => selector({ panel, close }),
}));

import { RiderSettlementCreateSheet } from "./RiderSettlementCreateSheet";

function renderSheet() {
  return renderWithIntl(<QueryClientProvider client={new QueryClient()}><RiderSettlementCreateSheet /></QueryClientProvider>);
}

beforeEach(() => {
  mutateAsync.mockReset();
  close.mockReset();
  panel = { mode: "rider-settlement-create" };
  session = { hasAllBranchesScope: true, branches: [] };
});

describe("RiderSettlementCreateSheet", () => {
  it("submits the selected branch, rider and cash received, then closes", async () => {
    mutateAsync.mockResolvedValue({ id: "rs1" });
    renderSheet();
    fireEvent.click(screen.getByLabelText("Branch"));
    fireEvent.click(await screen.findByRole("option", { name: "Kathmandu Hub" }));
    fireEvent.click(screen.getByLabelText("Rider"));
    fireEvent.click(await screen.findByRole("option", { name: "LIC-001" }));
    fireEvent.change(screen.getByLabelText("Cash received"), { target: { value: "2500" } });
    fireEvent.click(screen.getByRole("button", { name: /record deposit/i }));
    await waitFor(() => expect(mutateAsync).toHaveBeenCalledWith({ riderId: "r1", branchId: "b1", declaredAmount: 2500 }));
    await waitFor(() => expect(close).toHaveBeenCalled());
  });

  it("prefills from the holding row and previews variance live", async () => {
    panel = { mode: "rider-settlement-create", prefill: { riderId: "r1", branchId: "b1" } };
    renderSheet();
    expect(await screen.findByText(formatRs(2500))).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Cash received"), { target: { value: "2000" } });
    expect(screen.getByText(`Short − ${formatRs(500)}`)).toBeInTheDocument();
    expect(screen.getByText(/Shortage\/overage reasons and approval need backend support/)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Cash received"), { target: { value: "2500" } });
    expect(screen.getByText("Matched")).toBeInTheDocument();
  });

  it("shows and submits a prefilled rider who holds cash at a branch other than their base", async () => {
    mutateAsync.mockResolvedValue({ id: "rs2" });
    panel = { mode: "rider-settlement-create", prefill: { riderId: "r2", branchId: "b1" } };
    renderSheet();
    await waitFor(() => expect(screen.getByLabelText("Rider")).toHaveTextContent("LIC-002"));
    expect(screen.getByText(formatRs(1200))).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Cash received"), { target: { value: "1200" } });
    fireEvent.click(screen.getByRole("button", { name: /record deposit/i }));
    await waitFor(() => expect(mutateAsync).toHaveBeenCalledWith({ riderId: "r2", branchId: "b1", declaredAmount: 1200 }));
  });

  it("lists both riders based at the branch and riders holding cash there", async () => {
    renderSheet();
    fireEvent.click(screen.getByLabelText("Branch"));
    fireEvent.click(await screen.findByRole("option", { name: "Kathmandu Hub" }));
    fireEvent.click(screen.getByLabelText("Rider"));
    expect(await screen.findByRole("option", { name: "LIC-001" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "LIC-002" })).toBeInTheDocument();
    expect(screen.getAllByRole("option")).toHaveLength(2);
  });

  it("clears a rider who is not available at a newly chosen branch and blocks submit", async () => {
    panel = { mode: "rider-settlement-create", prefill: { riderId: "r2", branchId: "b1" } };
    renderSheet();
    await waitFor(() => expect(screen.getByLabelText("Rider")).toHaveTextContent("LIC-002"));
    fireEvent.click(screen.getByLabelText("Branch"));
    fireEvent.click(await screen.findByRole("option", { name: "Butwal Branch" }));
    await waitFor(() => expect(screen.getByLabelText("Rider")).toHaveTextContent("Select rider"));
    fireEvent.change(screen.getByLabelText("Cash received"), { target: { value: "1200" } });
    fireEvent.click(screen.getByRole("button", { name: /record deposit/i }));
    expect(await screen.findByText("Rider is required.")).toBeInTheDocument();
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it("offers only the branches in the session's scope", async () => {
    session = { hasAllBranchesScope: false, branches: [{ branchId: "b1" }] };
    renderSheet();
    fireEvent.click(screen.getByLabelText("Branch"));
    expect(await screen.findByRole("option", { name: "Kathmandu Hub" })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: "Pokhara Hub" })).toBeNull();
    expect(screen.queryByRole("option", { name: "Butwal Branch" })).toBeNull();
  });

  it("names the branch in the header", async () => {
    panel = { mode: "rider-settlement-create", prefill: { riderId: "r1", branchId: "b1" } };
    renderSheet();
    expect(await screen.findByText(/Kathmandu Hub · 3 COD parcels/)).toBeInTheDocument();
  });

  it.each([["", "Cash received is required."], ["0", "Cash received must be greater than zero."], ["-5", "Cash received must be greater than zero."], ["10.555", "Use at most 2 decimal places."]])(
    "rejects cash received %s without sending",
    async (value, message) => {
      panel = { mode: "rider-settlement-create", prefill: { riderId: "r1", branchId: "b1" } };
      renderSheet();
      fireEvent.change(screen.getByLabelText("Cash received"), { target: { value } });
      fireEvent.click(screen.getByRole("button", { name: /record deposit/i }));
      expect(await screen.findByText(message)).toBeInTheDocument();
      expect(mutateAsync).not.toHaveBeenCalled();
    },
  );

  it("keeps the sheet open and shows the server error", async () => {
    mutateAsync.mockRejectedValue(new Error(JSON.stringify({ error: { code: "COD_VALIDATION_FAILED", message: "No collected COD found for this rider at this branch." } })));
    panel = { mode: "rider-settlement-create", prefill: { riderId: "r1", branchId: "b1" } };
    renderSheet();
    fireEvent.change(screen.getByLabelText("Cash received"), { target: { value: "2500" } });
    fireEvent.click(screen.getByRole("button", { name: /record deposit/i }));
    expect(await screen.findByText("No collected COD found for this rider at this branch.")).toBeInTheDocument();
    expect(close).not.toHaveBeenCalled();
  });
});
