import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import ShipmentsPage from "./page";

vi.mock("@/features/shipments/hooks/useShipments", () => ({
  useShipments: () => ({ data: { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }, isLoading: false, isError: false, refetch: vi.fn() }),
}));
vi.mock("@/features/shipments/hooks/useShipmentsSummary", () => ({
  useShipmentsSummary: () => ({ data: { Created: 3, Delivered: 9 }, isLoading: false, isError: false }),
}));
vi.mock("@/features/shipments/hooks/useServiceTypes", () => ({ useServiceTypes: () => ({ data: [] }) }));
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: () => false }));
vi.mock("@/features/shipments/store/shipmentPanelStore", () => ({
  useShipmentPanelStore: (selector: (s: { openCreate: () => void; openDetail: () => void }) => unknown) =>
    selector({ openCreate: vi.fn(), openDetail: vi.fn() }),
}));

function wrapper() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={queryClient}>
      <ShipmentsPage />
    </QueryClientProvider>
  );
}

describe("ShipmentsPage", () => {
  it("renders the summary strip above the table using the summary hook's data", async () => {
    render(wrapper());
    await waitFor(() => expect(screen.getByText("Created")).toBeInTheDocument());
    expect(screen.getByText("3")).toBeInTheDocument();
    expect(screen.getByText("9")).toBeInTheDocument();
  });
});
