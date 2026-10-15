import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ManifestDraftSection } from "./ManifestDraftSection";
import * as clientFetchModule from "@/lib/api/clientFetch";
import type { Manifest } from "@/features/manifests/types";

const manifest: Manifest = {
  id: "m1",
  code: "MNF-20260829-001",
  fromBranchId: "b1",
  toBranchId: "b2",
  purpose: "Forward",
  vehiclePlate: null,
  driverId: null,
  status: "Draft",
  shipmentCount: 0,
  receivedCount: 0,
  createdAt: "2026-08-29T00:00:00Z",
};

function renderWithProviders(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

describe("ManifestDraftSection", () => {
  it("appends a successfully scanned AWB to the session list and clears the input", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue({
      id: "ms1",
      shipmentId: "s1",
      awb: "AWB123",
      scannedAt: "2026-08-29T00:00:00Z",
      receivedAt: null,
    });
    const user = userEvent.setup();
    renderWithProviders(<ManifestDraftSection manifest={manifest} />);

    const input = screen.getByLabelText(/scan awb/i);
    await user.type(input, "AWB123{Enter}");

    await waitFor(() => expect(screen.getByText("AWB123")).toBeInTheDocument());
    expect(input).toHaveValue("");
  });

  it("shows a plain-language error when the scan is rejected", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockRejectedValue(
      new Error(JSON.stringify({ error: { code: "MANIFEST_SCAN_REJECTED", reason: "SHIPMENT_NOT_FOUND" } }))
    );
    const user = userEvent.setup();
    renderWithProviders(<ManifestDraftSection manifest={manifest} />);

    const input = screen.getByLabelText(/scan awb/i);
    await user.type(input, "BADAWB{Enter}");

    await waitFor(() => expect(screen.getByText(/no shipment matches that awb/i)).toBeInTheDocument());
  });

  it("hides the seal button without manifest.seal", () => {
    renderWithProviders(<ManifestDraftSection manifest={manifest} canSeal={false} />);
    expect(screen.queryByRole("button", { name: /seal manifest/i })).not.toBeInTheDocument();
  });
});
