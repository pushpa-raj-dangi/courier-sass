import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import * as clientFetchModule from "@/lib/api/clientFetch";

vi.mock("@/features/shipments/hooks/useMerchants", () => ({ useMerchants: () => ({ data: [] }) }));
vi.mock("@/features/shipments/hooks/useServiceTypes", () => ({
  useServiceTypes: () => ({
    data: [
      { id: "st-standard", code: "Standard", name: "Standard" },
      { id: "st-express", code: "Express", name: "Express" },
    ],
  }),
}));
vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({
    data: [
      { id: "branch-ktm", code: "KTM-HUB", name: "Kathmandu Hub", isActive: true },
      { id: "branch-pkr", code: "PKR-HUB", name: "Pokhara Hub", isActive: true },
    ],
  }),
}));
vi.mock("@/features/shipments/hooks/useShipmentQuote", () => ({ useShipmentQuote: () => ({ data: null, isFetching: false }) }));
vi.mock("@/features/shipments/hooks/useCreateShipment", () => ({ useCreateShipment: () => ({ mutateAsync: vi.fn(), isPending: false }) }));
vi.mock("@/features/shipments/hooks/useShipmentDefaults", () => ({
  useShipmentDefaults: () => ({
    data: { defaultServiceTypeId: "st-standard", defaultOriginBranchId: "branch-ktm" },
  }),
}));

let panelMode: "create" | "closed" = "create";
vi.mock("@/features/shipments/store/shipmentPanelStore", () => ({
  useShipmentPanelStore: (selector: (s: unknown) => unknown) =>
    selector({ panel: { mode: panelMode }, close: vi.fn(), openDetail: vi.fn() }),
}));

import { ShipmentCreateSheet } from "./ShipmentCreateSheet";

// GeographyPicker (rendered for Recipient/Sender) hits the real useQuery-backed geography hooks,
// so the tree needs a QueryClientProvider; clientFetch is stubbed so those queries resolve
// harmlessly instead of hitting the network.
function renderWithProviders() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <ShipmentCreateSheet />
    </QueryClientProvider>
  );
}

describe("ShipmentCreateSheet", () => {
  it("pre-fills origin branch and service type from organization defaults when the sheet opens", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue([]);
    panelMode = "create";
    renderWithProviders();

    // Assert against the labeled Select trigger itself (toHaveTextContent), not a bare
    // screen.findByText: each Select also renders a visually-hidden native <select> mirroring the
    // same options for accessibility, and Origin/Destination share the same branches list, so a
    // plain text search matches multiple nodes even when only one field is actually pre-filled.
    expect(await screen.findByLabelText("Origin branch")).toHaveTextContent("Kathmandu Hub");
    expect(screen.getByLabelText("Service type")).toHaveTextContent("Standard");
    // Destination branch shares the same branches list but has no default — it must stay empty.
    expect(screen.getByLabelText("Destination branch")).not.toHaveTextContent("Kathmandu Hub");
  });

  it("never overwrites an origin branch the user already picked, even across re-renders", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue([]);
    panelMode = "create";
    renderWithProviders();

    // Wait for the default pre-fill to land first.
    await screen.findByLabelText("Origin branch");
    expect(screen.getByLabelText("Origin branch")).toHaveTextContent("Kathmandu Hub");

    // User overrides the pre-filled origin branch with a different one.
    fireEvent.click(screen.getByLabelText("Origin branch"));
    fireEvent.click(screen.getByRole("option", { name: "Pokhara Hub" }));
    expect(screen.getByLabelText("Origin branch")).toHaveTextContent("Pokhara Hub");

    // Force a re-render of the form (typing in an unrelated field triggers RHF/watch re-renders,
    // and the mocked useShipmentDefaults hook returns a fresh object identity on every call,
    // which would re-run the pre-fill effect if it weren't guarded). The manually chosen branch
    // must survive — the pre-fill must never re-apply once the field is non-empty.
    fireEvent.change(screen.getByLabelText("Weight (kg)"), { target: { value: "5" } });

    expect(screen.getByLabelText("Origin branch")).toHaveTextContent("Pokhara Hub");
    expect(screen.getByLabelText("Origin branch")).not.toHaveTextContent("Kathmandu Hub");
  });

  it("does not pre-fill when the sheet is closed", () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue([]);
    panelMode = "closed";
    renderWithProviders();

    expect(screen.queryByText("New Shipment")).not.toBeInTheDocument();
  });
});
