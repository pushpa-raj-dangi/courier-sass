import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import CodPage from "./page";

vi.mock("@/features/cod/hooks/useCodSummary", () => ({
  useCodSummary: () => ({ data: { expected: 5000, collected: 3000, pending: 2000, settled: 1000, merchantPayable: 900 }, isLoading: false, isError: false }),
}));
vi.mock("@/features/cod/components/CollectionsTab", () => ({ CollectionsTab: () => null }));
vi.mock("@/features/cod/components/CollectionDetailSheet", () => ({ CollectionDetailSheet: () => null }));
vi.mock("@/features/cod/components/RiderSettlementsTab", () => ({ RiderSettlementsTab: () => null }));
vi.mock("@/features/cod/components/RiderSettlementCreateSheet", () => ({ RiderSettlementCreateSheet: () => null }));
vi.mock("@/features/cod/components/RiderSettlementDetailSheet", () => ({ RiderSettlementDetailSheet: () => null }));
vi.mock("@/features/cod/components/BranchSettlementsTab", () => ({ BranchSettlementsTab: () => null }));
vi.mock("@/features/cod/components/BranchSettlementCreateSheet", () => ({ BranchSettlementCreateSheet: () => null }));
vi.mock("@/features/cod/components/BranchSettlementDetailSheet", () => ({ BranchSettlementDetailSheet: () => null }));
vi.mock("@/features/cod/components/MerchantSettlementsTab", () => ({ MerchantSettlementsTab: () => null }));
vi.mock("@/features/cod/components/MerchantSettlementCreateSheet", () => ({ MerchantSettlementCreateSheet: () => null }));
vi.mock("@/features/cod/components/MerchantSettlementDetailSheet", () => ({ MerchantSettlementDetailSheet: () => null }));
vi.mock("@/features/cod/components/AdjustmentsTab", () => ({ AdjustmentsTab: () => null }));
vi.mock("@/features/cod/components/AdjustmentDetailSheet", () => ({ AdjustmentDetailSheet: () => null }));
vi.mock("@/features/cod/components/ReconciliationTab", () => ({ ReconciliationTab: () => null }));

describe("CodPage", () => {
  it("renders the financial summary strip above the tabs", () => {
    render(<CodPage />);
    expect(screen.getByText("Expected")).toBeInTheDocument();
    expect(screen.getByText("Pending")).toBeInTheDocument();
  });
});
