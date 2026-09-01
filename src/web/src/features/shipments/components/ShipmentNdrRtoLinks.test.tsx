import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ShipmentNdrRtoLinks } from "./ShipmentNdrRtoLinks";
import * as clientFetchModule from "@/lib/api/clientFetch";

function renderWithProviders(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

describe("ShipmentNdrRtoLinks", () => {
  it("shows a View NDR button when an unresolved NDR exists for the shipment", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockImplementation(async (path: unknown) => {
      if (typeof path === "string" && path.startsWith("/api/ndrs")) {
        return {
          items: [{
            id: "n1", shipmentId: "s1", awb: "AWB001", reason: "CustomerUnavailable",
            assignedStaffUserId: null, status: "Open", customerResponse: null, followUpAt: null,
            nextAction: null, resolution: null, resolvedAt: null, createdAt: "2026-09-01T00:00:00Z",
          }],
          page: 1, pageSize: 50, totalCount: 1, totalPages: 1,
        };
      }
      return [];
    });

    renderWithProviders(<ShipmentNdrRtoLinks shipmentId="s1" />);

    await waitFor(() => expect(screen.getByRole("button", { name: /view ndr/i })).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /view return shipment/i })).not.toBeInTheDocument();
  });

  it("renders nothing when there is no NDR or return shipment", async () => {
    const spy = vi.spyOn(clientFetchModule, "clientFetch").mockImplementation(async (path: unknown) => {
      if (typeof path === "string" && path.startsWith("/api/ndrs")) {
        return { items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 };
      }
      return [];
    });

    const { container } = renderWithProviders(<ShipmentNdrRtoLinks shipmentId="s1" />);

    await waitFor(() => expect(spy).toHaveBeenCalled());
    expect(container).toBeEmptyDOMElement();
  });
});
