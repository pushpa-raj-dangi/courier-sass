import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/merchant/pickups",
  useSearchParams: () => new URLSearchParams(search),
}));

const clientFetchMock = vi.fn();
vi.mock("@/lib/api/clientFetch", () => ({ clientFetch: (...a: unknown[]) => clientFetchMock(...a) }));

let canCreate = true;
vi.mock("@/features/merchant-auth/hooks/useMerchantPermission", () => ({
  useMerchantPermission: (p: string) => (p === "merchant.pickups.create" ? canCreate : true),
}));

import { useMerchantPickupPanelStore } from "@/features/merchant-pickups/store/merchantPickupPanelStore";
import { MerchantPickupsPage } from "./MerchantPickupsPage";
import type { PickupRequest } from "../types";

function pad(n: number) {
  return String(n).padStart(2, "0");
}
function dayOffset(offset: number) {
  const d = new Date(Date.now() + offset * 86_400_000);
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`;
}

const parcel = (n: number) => ({ id: `s${n}`, awb: `QA-${100 + n}`, destinationArea: "Pokhara", codAmount: null });
const pickup = (over: Partial<PickupRequest>): PickupRequest => ({
  id: "p1",
  merchantId: "m1",
  branchId: "b1",
  shipmentIds: [],
  requestedForDate: dayOffset(2),
  notes: null,
  status: "Requested",
  createdAt: "2026-11-01T00:00:00Z",
  updatedAt: "2026-11-01T00:00:00Z",
  stage: "Requested",
  branchName: "Kathmandu Hub",
  shipments: [],
  ...over,
});

const upcoming = pickup({ id: "p1", shipments: [1, 2, 3, 4, 5].map(parcel), notes: "Ring the bell" });
const stale = pickup({ id: "p2", requestedForDate: dayOffset(-3), stage: "Scheduled", status: "Scheduled", shipments: [parcel(6)] });
const done = pickup({ id: "p3", requestedForDate: dayOffset(-10), stage: "Cancelled", status: "Cancelled" });

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MerchantPickupsPage />
    </QueryClientProvider>
  );
}

beforeEach(() => {
  search = "";
  canCreate = true;
  replace.mockReset();
  clientFetchMock.mockReset();
  clientFetchMock.mockResolvedValue([upcoming, stale, done]);
  useMerchantPickupPanelStore.setState({ panel: { mode: "closed" } });
});

describe("MerchantPickupsPage", () => {
  it("shows tab counts and tile values", async () => {
    renderPage();
    expect(await screen.findByRole("tab", { name: "Upcoming 2" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Past 1" })).toBeInTheDocument();
    expect(screen.getByText("Next pickup")).toBeInTheDocument();
    expect(screen.getByText(/Kathmandu Hub · 5 parcels/)).toBeInTheDocument();
    expect(screen.getByText("Waiting to be scheduled").nextElementSibling).toHaveTextContent("1");
  });

  it("says None booked when no open pickup is ahead", async () => {
    clientFetchMock.mockResolvedValue([done]);
    renderPage();
    expect(await screen.findByText("None booked")).toBeInTheDocument();
  });

  it("flags an open pickup whose date has passed", async () => {
    renderPage();
    expect(await screen.findByText("Date passed")).toBeInTheDocument();
    expect(screen.getByText("In 2 days")).toBeInTheDocument();
  });

  it("shows two AWBs and the remainder in the parcels cell, with no Cancel button", async () => {
    renderPage();
    expect(await screen.findByText("QA-101, QA-102 +3")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /cancel/i })).not.toBeInTheDocument();
  });

  it("opens the detail Sheet when a row is clicked", async () => {
    renderPage();
    const cell = await screen.findByText("Ring the bell");
    fireEvent.click(within(cell.closest("tr") as HTMLElement).getByText("5 parcels"));
    expect(useMerchantPickupPanelStore.getState().panel).toEqual({ mode: "detail", id: "p1" });
  });

  it("shows past pickups under ?tab=past and switches tabs via the URL", async () => {
    search = "tab=past";
    renderPage();
    expect(await screen.findByText("Cancelled")).toBeInTheDocument();
    fireEvent.mouseDown(screen.getByRole("tab", { name: /Upcoming/ }));
    expect(replace).toHaveBeenCalledWith("/merchant/pickups", { scroll: false });
  });

  it("shows an empty state for an empty tab", async () => {
    clientFetchMock.mockResolvedValue([]);
    renderPage();
    expect(await screen.findByText("No pickups coming up")).toBeInTheDocument();
  });

  it("shows a retryable error and dashes while the list fails", async () => {
    clientFetchMock.mockRejectedValue(new Error("boom"));
    renderPage();
    expect(await screen.findByRole("alert")).toHaveTextContent("Couldn't load pickup requests");
    expect(screen.getByRole("button", { name: "Retry" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Upcoming —" })).toBeInTheDocument();
    expect(screen.getByText("Waiting to be scheduled").nextElementSibling).toHaveTextContent("—");
  });

  it("hides Request pickup without the create permission", async () => {
    canCreate = false;
    renderPage();
    await screen.findByRole("tab", { name: "Upcoming 2" });
    expect(screen.queryByRole("button", { name: "Request pickup" })).not.toBeInTheDocument();
  });
});
