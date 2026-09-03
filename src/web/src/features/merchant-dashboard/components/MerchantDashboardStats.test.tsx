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
        { id: "s3", currentStatus: "InTransit" },
        { id: "s4", currentStatus: "OutForDelivery" },
        { id: "s5", currentStatus: "Delivered" },
      ],
      page: 1, pageSize: 100, totalCount: 5, totalPages: 1,
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
  it("counts shipments by status group", async () => {
    renderWithClient();

    await waitFor(() => expect(screen.getByText("2")).toBeInTheDocument()); // Created
    // "1" alone is ambiguous here (Out for Delivery and Delivered are also 1), so scope to the
    // In Transit tile specifically, following the label -> value DOM lookup used elsewhere
    // (see QuickStatsStrip.test.tsx) rather than a bare getByText("1").
    const inTransitLabel = screen.getByText("In Transit");
    expect(inTransitLabel.parentElement?.nextElementSibling).toHaveTextContent("1");
  });

  it("sums COD pending and settled from settlements", async () => {
    renderWithClient();

    await waitFor(() => expect(screen.getByText("NPR 1,500.00")).toBeInTheDocument());
    expect(screen.getByText("NPR 3,000.00")).toBeInTheDocument();
  });
});
