import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen } from "@testing-library/react";
import { renderWithIntl as render } from "@/test/renderWithIntl";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/cod",
  useSearchParams: () => new URLSearchParams(search),
}));

const useCodFlow = vi.fn();
vi.mock("@/features/cod/hooks/useCodFlow", () => ({ useCodFlow: (p: unknown) => useCodFlow(p) }));
let session = { hasAllBranchesScope: false, branches: [{ branchId: "b1" }] };
vi.mock("@/features/auth/hooks/useSession", () => ({ useSession: () => ({ data: session }) }));
vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b1", name: "Lalitpur Branch" }, { id: "b2", name: "Pokhara Hub" }] }),
}));
vi.mock("@/features/cod/components/CollectionsTab", () => ({ CollectionsTab: () => <div>collections-tab</div> }));
vi.mock("@/features/cod/components/RiderDepositsTab", () => ({ RiderDepositsTab: () => <div>rider-deposits-tab</div> }));
vi.mock("@/features/cod/components/BranchSettlementsTab", () => ({ BranchSettlementsTab: () => <div>branch-tab</div> }));
vi.mock("@/features/cod/components/MerchantSettlementsTab", () => ({ MerchantSettlementsTab: () => <div>merchant-tab</div> }));
vi.mock("@/features/cod/components/AdjustmentsTab", () => ({ AdjustmentsTab: () => <div>adjustments-tab</div> }));
vi.mock("@/features/cod/components/CollectionDetailSheet", () => ({ CollectionDetailSheet: () => null }));
vi.mock("@/features/cod/components/RiderSettlementCreateSheet", () => ({ RiderSettlementCreateSheet: () => null }));
vi.mock("@/features/cod/components/RiderSettlementDetailSheet", () => ({ RiderSettlementDetailSheet: () => null }));
vi.mock("@/features/cod/components/BranchSettlementCreateSheet", () => ({ BranchSettlementCreateSheet: () => null }));
vi.mock("@/features/cod/components/BranchSettlementDetailSheet", () => ({ BranchSettlementDetailSheet: () => null }));
vi.mock("@/features/cod/components/MerchantSettlementCreateSheet", () => ({ MerchantSettlementCreateSheet: () => null }));
vi.mock("@/features/cod/components/MerchantSettlementDetailSheet", () => ({ MerchantSettlementDetailSheet: () => null }));
vi.mock("@/features/cod/components/AdjustmentDetailSheet", () => ({ AdjustmentDetailSheet: () => null }));

import CodPage from "./page";

beforeEach(() => {
  replace.mockReset();
  useCodFlow.mockReset();
  useCodFlow.mockReturnValue({ data: undefined, isLoading: true, isError: false, refetch: vi.fn() });
  search = "";
  session = { hasAllBranchesScope: false, branches: [{ branchId: "b1" }] };
});

describe("CodPage", () => {
  it("defaults to the rider deposits tab and a 7-day range", () => {
    render(<CodPage />);
    expect(screen.getByRole("heading", { name: "COD reconciliation" })).toBeInTheDocument();
    expect(screen.getByText("rider-deposits-tab")).toBeInTheDocument();
    expect(screen.getByText(/All my branches · Last 7 days/)).toBeInTheDocument();
    const params = useCodFlow.mock.calls[0][0] as { from: string; to: string };
    expect(params.from).toMatch(/T00:00:00Z$/);
    expect(params.to).toMatch(/T23:59:59Z$/);
  });

  it("reads the tab from the URL and writes tab changes back", () => {
    search = "tab=ledger";
    render(<CodPage />);
    expect(screen.getByRole("region", { name: "Ledger" })).toBeInTheDocument();
    expect(screen.getByText(/Every COD money movement/)).toBeInTheDocument();
    fireEvent.mouseDown(screen.getByRole("tab", { name: "Adjustments" }));
    expect(replace).toHaveBeenCalledWith("/cod?tab=adjustments", { scroll: false });
  });

  it("hides the branch picker for single-branch users and shows it for multi-branch users", () => {
    render(<CodPage />);
    expect(screen.queryByRole("combobox", { name: "Branch" })).toBeNull();
    session = { hasAllBranchesScope: false, branches: [{ branchId: "b1" }, { branchId: "b2" }] };
    render(<CodPage />);
    expect(screen.getByRole("combobox", { name: "Branch" })).toBeInTheDocument();
  });

  it("passes the URL branch to the flow query", () => {
    search = "branch=b1";
    render(<CodPage />);
    expect(useCodFlow.mock.calls[0][0]).toMatchObject({ branchId: "b1" });
    expect(screen.getByText(/Lalitpur Branch · Last 7 days/)).toBeInTheDocument();
  });

  it("renders the needs-backend actions disabled", () => {
    render(<CodPage />);
    expect(screen.getByRole("button", { name: "Download ledger" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Close branch day" })).toBeDisabled();
  });
});
