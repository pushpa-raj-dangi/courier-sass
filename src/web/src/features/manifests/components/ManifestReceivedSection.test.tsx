import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ManifestReceivedSection } from "./ManifestReceivedSection";
import * as clientFetchModule from "@/lib/api/clientFetch";
import type { Manifest } from "@/features/manifests/types";

const manifest: Manifest = {
  id: "m1",
  code: "MNF-20260829-001",
  fromBranchId: "b1",
  toBranchId: "b2",
  purpose: "Forward",
  vehiclePlate: "BA 2 KHA 1234",
  driverId: null,
  status: "Received",
  shipmentCount: 2,
  receivedCount: 0,
  createdAt: "2026-08-29T00:00:00Z",
};

function renderWithProviders() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <ManifestReceivedSection manifest={manifest} />
    </QueryClientProvider>
  );
}

describe("ManifestReceivedSection", () => {
  it("shows the missing-shipments list from the server", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue([
      { shipmentId: "s1", awb: "AWB999", scannedAt: "2026-08-29T00:00:00Z" },
    ]);
    renderWithProviders();

    await waitFor(() => expect(screen.getByText("AWB999")).toBeInTheDocument());
  });

  it("shows no missing shipments when the list is empty", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue([]);
    renderWithProviders();

    await waitFor(() => expect(screen.getByText(/no missing shipments/i)).toBeInTheDocument());
  });
});
