import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const clientFetchMock = vi.fn();
vi.mock("@/lib/api/clientFetch", () => ({ clientFetch: (...a: unknown[]) => clientFetchMock(...a) }));

let canCancel = true;
vi.mock("@/features/merchant-auth/hooks/useMerchantPermission", () => ({
  useMerchantPermission: (p: string) => (p === "merchant.pickups.cancel" ? canCancel : true),
}));

import { useMerchantPickupPanelStore } from "@/features/merchant-pickups/store/merchantPickupPanelStore";
import { useMerchantShipmentPanelStore } from "@/features/merchant-shipments/store/merchantShipmentPanelStore";
import { MerchantPickupDetailSheet } from "./MerchantPickupDetailSheet";
import type { PickupRequest } from "../types";

const base: PickupRequest = {
  id: "p1",
  merchantId: "m1",
  branchId: "b1",
  shipmentIds: [],
  requestedForDate: "2026-11-20",
  notes: null,
  status: "Requested",
  createdAt: "2026-11-01T00:00:00Z",
  updatedAt: "2026-11-01T00:00:00Z",
  stage: "Requested",
  branchName: "Kathmandu Hub",
  shipments: [
    { id: "s1", awb: "QA-101", destinationArea: "Pokhara", codAmount: null },
    { id: "s2", awb: "QA-102", destinationArea: null, codAmount: 2500 },
  ],
};

function renderSheet(pickup: PickupRequest = base) {
  clientFetchMock.mockImplementation((path: string) =>
    path === "/api/merchant/pickups" ? Promise.resolve([pickup]) : Promise.resolve(pickup)
  );
  useMerchantPickupPanelStore.setState({ panel: { mode: "detail", id: pickup.id } });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MerchantPickupDetailSheet />
    </QueryClientProvider>
  );
}

beforeEach(() => {
  canCancel = true;
  clientFetchMock.mockReset();
  useMerchantPickupPanelStore.setState({ panel: { mode: "closed" } });
  useMerchantShipmentPanelStore.setState({ panel: { mode: "closed" } });
});

describe("MerchantPickupDetailSheet", () => {
  it("titles the Sheet with the branch and parcel count", async () => {
    renderSheet();
    expect(await screen.findByText("Kathmandu Hub collects 2 parcels")).toBeInTheDocument();
  });

  it("uses the date as title, the request date in the eyebrow, and the note as its own block", async () => {
    renderSheet({ ...base, notes: "Ring the shop bell" });
    expect(await screen.findByRole("heading", { name: /Fri, 20 Nov/ })).toBeInTheDocument();
    expect(screen.getByText("PICKUP · REQUESTED 1 NOV 2026")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Your note to the rider" })).toHaveTextContent("Ring the shop bell");
  });

  it("omits the note block when there is no note, and shows the cancel hint", async () => {
    renderSheet();
    await screen.findByText("Kathmandu Hub collects 2 parcels");
    expect(screen.queryByText("Your note to the rider")).not.toBeInTheDocument();
    expect(screen.getByText("You can cancel until the branch schedules a rider")).toBeInTheDocument();
  });

  it("marks progress to the current stage", async () => {
    renderSheet({ ...base, stage: "Scheduled", status: "Scheduled" });
    const steps = await screen.findAllByRole("listitem");
    expect(steps.map((s) => s.textContent)).toEqual(["Requested", "Scheduled", "Collected"]);
    expect(steps[1]).toHaveAttribute("aria-current", "step");
    expect(steps[2]).not.toHaveAttribute("aria-current");
  });

  it("shows the Cancelled state instead of the progress line", async () => {
    renderSheet({ ...base, stage: "Cancelled", status: "Cancelled" });
    await screen.findByText("Kathmandu Hub collects 2 parcels");
    expect(screen.queryByRole("list", { name: "Pickup progress" })).not.toBeInTheDocument();
    expect(screen.getAllByText("Cancelled").length).toBeGreaterThan(0);
  });

  it("lists parcels with Prepaid and formatted COD, and opens a shipment by AWB", async () => {
    renderSheet();
    expect(await screen.findByText("Prepaid")).toBeInTheDocument();
    expect(screen.getByText("Rs 2,500")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "QA-102" }));
    expect(useMerchantShipmentPanelStore.getState().panel).toEqual({ mode: "detail", awb: "QA-102" });
  });

  it("offers Cancel only for a Requested pickup with the permission", async () => {
    const { unmount } = renderSheet();
    expect(await screen.findByRole("button", { name: "Cancel pickup" })).toBeInTheDocument();
    unmount();
    renderSheet({ ...base, stage: "Scheduled", status: "Scheduled" });
    await screen.findByText("Kathmandu Hub collects 2 parcels");
    expect(screen.queryByRole("button", { name: "Cancel pickup" })).not.toBeInTheDocument();
  });

  it("hides Cancel without the permission", async () => {
    canCancel = false;
    renderSheet();
    await screen.findByText("Kathmandu Hub collects 2 parcels");
    expect(screen.queryByRole("button", { name: "Cancel pickup" })).not.toBeInTheDocument();
  });

  it("confirms, then POSTs the cancel", async () => {
    renderSheet();
    fireEvent.click(await screen.findByRole("button", { name: "Cancel pickup" }));
    expect(await screen.findByText(/Cancel the pickup on 20 Nov 2026\?/)).toBeInTheDocument();
    expect(screen.getByText("The branch won't send a rider.")).toBeInTheDocument();
    fireEvent.click(screen.getAllByRole("button", { name: "Cancel pickup" }).at(-1)!);
    await waitFor(() =>
      expect(clientFetchMock).toHaveBeenCalledWith("/api/merchant/pickups/p1/cancel", { method: "POST" })
    );
  });

  it("shows the backend message on a 400 and stays open", async () => {
    renderSheet();
    clientFetchMock.mockImplementation((path: string) =>
      path.endsWith("/cancel")
        ? Promise.reject(
            new Error(
              JSON.stringify({
                error: { code: "SHIPMENT_VALIDATION_FAILED", message: "A rider has already collected parcels from this pickup" },
              })
            )
          )
        : Promise.resolve([base])
    );
    fireEvent.click(await screen.findByRole("button", { name: "Cancel pickup" }));
    fireEvent.click(screen.getAllByRole("button", { name: "Cancel pickup" }).at(-1)!);
    expect(await screen.findByText("A rider has already collected parcels from this pickup")).toBeInTheDocument();
    const listCalls = () => clientFetchMock.mock.calls.filter((c) => c[0] === "/api/merchant/pickups").length;
    await waitFor(() => expect(listCalls()).toBeGreaterThan(1));
    expect(useMerchantPickupPanelStore.getState().panel).toEqual({ mode: "detail", id: "p1" });
  });
});
