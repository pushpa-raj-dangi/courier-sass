import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MerchantDashboardStats } from "./MerchantDashboardStats";

vi.mock("@/features/merchant-shipments/hooks/useMerchantShipments", () => ({
  useMerchantShipments: () => ({
    data: {
      items: [
        { id: "s1", currentStatus: "Created" },
        { id: "s2", currentStatus: "Created" },
        // PickupAssigned must land in the "Created" tile, matching statusConfig.ts's
        // STATUS_CONFIG, which gives PickupAssigned the same "Created" label as Created
        // everywhere else in the app.
        { id: "s6", currentStatus: "PickupAssigned" },
        { id: "s3", currentStatus: "InTransit" },
        { id: "s4", currentStatus: "OutForDelivery" },
        { id: "s5", currentStatus: "Delivered" },
      ],
      page: 1, pageSize: 100, totalCount: 6, totalPages: 1,
    },
  }),
}));

vi.mock("@/features/merchant-cod/hooks/useMerchantSettlements", () => ({
  useMerchantSettlements: () => ({
    data: [
      { id: "st1", status: "Created", totalPayable: 1500 },
      { id: "st2", status: "Settled", totalPayable: 3000 },
    ],
  }),
}));

function renderWithClient() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MerchantDashboardStats />
    </QueryClientProvider>
  );
}

describe("MerchantDashboardStats", () => {
  it("counts shipments by status group, folding PickupAssigned into Created", async () => {
    renderWithClient();

    // Created tile: 2 "Created" rows + 1 "PickupAssigned" row = 3.
    await waitFor(() => expect(screen.getByText("3")).toBeInTheDocument());
    const createdLabel = screen.getByText("Created");
    expect(createdLabel.parentElement?.nextElementSibling).toHaveTextContent("3");

    // "1" alone is ambiguous here (Out for Delivery and Delivered are also 1), so scope to the
    // In Transit tile specifically, following the label -> value DOM lookup used elsewhere
    // (see QuickStatsStrip.test.tsx) rather than a bare getByText("1"). This also confirms the
    // PickupAssigned row was NOT double-counted into In Transit.
    const inTransitLabel = screen.getByText("In Transit");
    expect(inTransitLabel.parentElement?.nextElementSibling).toHaveTextContent("1");
  });

  it("sums COD pending and settled from settlements", async () => {
    renderWithClient();

    await waitFor(() => expect(screen.getByText("NPR 1,500.00")).toBeInTheDocument());
    expect(screen.getByText("NPR 3,000.00")).toBeInTheDocument();
  });
});
