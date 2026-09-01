import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ReturnShipmentDetailSheet } from "./ReturnShipmentDetailSheet";
import { useReturnShipmentPanelStore } from "@/features/ndr/store/returnShipmentPanelStore";
import * as clientFetchModule from "@/lib/api/clientFetch";
import type { ReturnShipment } from "@/features/ndr/types";

const returnShipment: ReturnShipment = {
  id: "rs1", originalShipmentId: "s1", originalAwb: "AWB001", returnAwb: "RTO-20260901-001",
  rtoReason: "Customer refused", originBranchId: "b2", destinationBranchId: "b1",
  currentStatus: "RtoInTransit", currentBranchId: null, merchantConfirmedAt: null,
};

function mockSessionWithPermissions(permissions: string[]) {
  global.fetch = vi.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => ({
      userId: "u1", organizationId: "o1", email: "a@b.com", fullName: "A B",
      permissions, hasAllBranchesScope: true, branches: [],
    }),
  }) as unknown as typeof fetch;
}

function renderWithProviders(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

describe("ReturnShipmentDetailSheet", () => {
  it("disables Confirm Return until the shipment reaches RtoAtOrigin", async () => {
    mockSessionWithPermissions(["rto.manage"]);
    vi.spyOn(clientFetchModule, "clientFetch").mockImplementation(async (path: unknown) => {
      if (typeof path === "string" && path.startsWith("/api/return-shipments")) {
        return returnShipment;
      }
      return [];
    });
    useReturnShipmentPanelStore.setState({ panel: { mode: "detail", id: "rs1" } });

    renderWithProviders(<ReturnShipmentDetailSheet />);

    await waitFor(() => expect(screen.getByRole("button", { name: /confirm return/i })).toBeDisabled());
  });

  it("enables Confirm Return once the shipment is RtoAtOrigin", async () => {
    mockSessionWithPermissions(["rto.manage"]);
    vi.spyOn(clientFetchModule, "clientFetch").mockImplementation(async (path: unknown) => {
      if (typeof path === "string" && path.startsWith("/api/return-shipments")) {
        return { ...returnShipment, currentStatus: "RtoAtOrigin" };
      }
      return [];
    });
    useReturnShipmentPanelStore.setState({ panel: { mode: "detail", id: "rs1" } });

    renderWithProviders(<ReturnShipmentDetailSheet />);

    await waitFor(() => expect(screen.getByRole("button", { name: /confirm return/i })).toBeEnabled());
  });
});
