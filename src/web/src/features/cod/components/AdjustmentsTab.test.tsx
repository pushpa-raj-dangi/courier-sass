import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { formatRs } from "@/features/cod/lib/codFormat";

vi.mock("@/features/cod/hooks/useAdjustments", () => ({
  useAdjustments: () => ({
    data: { items: [{ id: "a1", entityType: "MerchantSettlementItem", entityId: "i1", beforeValue: 0, afterValue: 1250.5, reason: "Damaged parcel", requestedByUserId: "u1", requestedAt: "2026-10-06T00:00:00Z", approvedByUserId: null, approvedAt: null, applied: false }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 },
    isLoading: false,
  }),
}));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({ usePeopleNames: () => ({ userName: () => "Ramesh Shrestha", riderName: () => "Rider", branchName: () => "Branch" }) }));
vi.mock("@/features/cod/store/codPanelStore", () => ({ useCodPanelStore: (sel: (s: unknown) => unknown) => sel({ openAdjustmentDetail: vi.fn() }) }));

import { AdjustmentsTab } from "./AdjustmentsTab";

describe("AdjustmentsTab", () => {
  it("formats money consistently and names the requester", () => {
    render(<AdjustmentsTab />);
    expect(screen.getByText(formatRs(1250.5))).toBeInTheDocument();
    expect(screen.getByText("Ramesh Shrestha")).toBeInTheDocument();
    expect(screen.getByText("Pending")).toBeInTheDocument();
    expect(screen.queryByText(/Rs\. /)).toBeNull();
    expect(screen.queryByText(/not filtered by branch/)).toBeNull();
  });

  it("says it is merchant-level when the page has a branch selected", () => {
    render(<AdjustmentsTab branchFiltered />);
    expect(screen.getByText("Merchant-level — not filtered by branch")).toBeInTheDocument();
  });
});
