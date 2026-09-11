import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { CommandPalette } from "./CommandPalette";
import { useCommandPaletteStore } from "@/lib/store/commandPaletteStore";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";
import { useBranchPanelStore } from "@/features/branches/store/branchPanelStore";
import { EMPTY_SEARCH_RESULTS } from "@/features/search/types";

const push = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push }),
}));

const useGlobalSearchMock = vi.fn();
vi.mock("@/features/search/hooks/useGlobalSearch", () => ({
  useGlobalSearch: (query: string) => useGlobalSearchMock(query),
}));

function renderPalette(permissions: string[]) {
  global.fetch = vi.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => ({
      userId: "u1",
      organizationId: "o1",
      email: "a@b.com",
      fullName: "A B",
      permissions,
      hasAllBranchesScope: true,
      branches: [],
    }),
  }) as unknown as typeof fetch;

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <CommandPalette />
    </QueryClientProvider>
  );
}

describe("CommandPalette", () => {
  beforeEach(() => {
    push.mockClear();
    useCommandPaletteStore.setState({ open: false });
    useShipmentPanelStore.setState({ panel: { mode: "closed" } });
    useBranchPanelStore.setState({ panel: { mode: "closed" } });
    useGlobalSearchMock.mockReset();
    useGlobalSearchMock.mockReturnValue({ data: EMPTY_SEARCH_RESULTS, isFetching: false });
  });

  it("is closed until Ctrl+K is pressed", async () => {
    renderPalette(["report.read"]);
    expect(screen.queryByPlaceholderText(/search or type a command/i)).not.toBeInTheDocument();

    fireEvent.keyDown(window, { key: "k", ctrlKey: true });

    expect(await screen.findByPlaceholderText(/search or type a command/i)).toBeInTheDocument();
  });

  it("shows only Pages the session has permission for", async () => {
    renderPalette(["report.read"]);
    fireEvent.keyDown(window, { key: "k", ctrlKey: true });

    await screen.findByText("Dashboard");
    expect(screen.queryByText("Shipments")).not.toBeInTheDocument();
  });

  it("navigates and closes the dialog when a page is selected", async () => {
    const user = userEvent.setup();
    renderPalette(["report.read", "shipment.read"]);
    fireEvent.keyDown(window, { key: "k", ctrlKey: true });

    const item = await screen.findByText("Shipments");
    await user.click(item);

    expect(push).toHaveBeenCalledWith("/shipments");
    await waitFor(() => expect(screen.queryByPlaceholderText(/search or type a command/i)).not.toBeInTheDocument());
  });

  it("runs the New Shipment quick action and closes the dialog", async () => {
    const user = userEvent.setup();
    renderPalette(["report.read", "shipment.create"]);
    fireEvent.keyDown(window, { key: "k", ctrlKey: true });

    const item = await screen.findByText("New Shipment");
    await user.click(item);

    expect(useShipmentPanelStore.getState().panel).toEqual({ mode: "create" });
    await waitFor(() => expect(screen.queryByPlaceholderText(/search or type a command/i)).not.toBeInTheDocument());
  });

  it("hides quick actions the session lacks permission for", async () => {
    renderPalette(["report.read", "shipment.create"]);
    fireEvent.keyDown(window, { key: "k", ctrlKey: true });

    await screen.findByText("New Shipment");
    expect(screen.queryByText("New Branch")).not.toBeInTheDocument();
  });

  it("switches from quick actions to entity search once a query is typed, showing the not-yet-available note", async () => {
    const user = userEvent.setup();
    renderPalette(["report.read", "shipment.create"]);
    fireEvent.keyDown(window, { key: "k", ctrlKey: true });

    await screen.findByText("New Shipment");

    const input = screen.getByPlaceholderText(/search or type a command/i);
    await user.type(input, "AWB123");

    await waitFor(() => expect(screen.queryByText("New Shipment")).not.toBeInTheDocument());
    expect(screen.getByText(/entity search isn.t available yet/i)).toBeInTheDocument();
  });

  it("opens a shipment's detail sheet and closes the dialog when an entity result is selected", async () => {
    const user = userEvent.setup();
    useGlobalSearchMock.mockReturnValue({
      data: {
        ...EMPTY_SEARCH_RESULTS,
        shipments: [{ awb: "AWB123", recipientName: "Jane Doe", currentStatus: "IN_TRANSIT" }],
      },
      isFetching: false,
    });
    renderPalette(["report.read", "shipment.read"]);
    fireEvent.keyDown(window, { key: "k", ctrlKey: true });

    const input = screen.getByPlaceholderText(/search or type a command/i);
    await user.type(input, "AWB123");

    const result = await screen.findByText("AWB123");
    await user.click(result);

    expect(useShipmentPanelStore.getState().panel).toEqual({ mode: "detail", awb: "AWB123" });
    await waitFor(() => expect(screen.queryByPlaceholderText(/search or type a command/i)).not.toBeInTheDocument());
  });
});
