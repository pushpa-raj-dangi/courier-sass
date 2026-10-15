import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, within } from "@testing-library/react";
import { IntlTestProvider, renderWithIntl as render } from "@/test/renderWithIntl";
import type { ReturnShipment } from "@/features/ndr/types";
import type { Shipment, ShipmentEvent } from "@/features/shipments/types";

let returnQuery: { data?: ReturnShipment; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/ndr/hooks/useReturnShipment", () => ({ useReturnShipment: () => returnQuery }));
const confirmMutate = vi.fn();
const panelState = { id: "r1" };
vi.mock("@/features/ndr/hooks/useConfirmReturnShipment", () => ({ useConfirmReturnShipment: () => ({ mutate: confirmMutate, isPending: false, isError: false }) }));
let shipmentQuery: { data?: Shipment; isError: boolean };
vi.mock("@/features/shipments/hooks/useShipment", () => ({ useShipment: () => shipmentQuery }));
const useShipmentEvents = vi.fn();
vi.mock("@/features/shipments/hooks/useShipmentEvents", () => ({ useShipmentEvents: (awb: string) => useShipmentEvents(awb) }));
vi.mock("@/features/shipments/hooks/useMerchants", () => ({ useMerchants: () => ({ data: [{ id: "mer1", displayName: "Daraz Seller", isActive: true }] }) }));
vi.mock("@/features/shipments/components/detail/TimelineTab", () => ({
  TimelineTab: ({ events }: { events: ShipmentEvent[] }) => <p>{`timeline with ${events.length} events`}</p>,
}));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ branchName: (id: string | null) => (id === "b1" ? "Kathmandu Hub" : "Pokhara Hub"), userName: () => "Staff", riderName: () => "Rider" }),
}));
let granted: string[] = [];
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => granted.includes(p) }));
const openShipment = vi.fn();
vi.mock("@/features/shipments/store/shipmentPanelStore", () => ({
  useShipmentPanelStore: (selector: (s: unknown) => unknown) => selector({ openDetail: openShipment }),
}));
vi.mock("@/features/ndr/store/returnShipmentPanelStore", () => ({
  useReturnShipmentPanelStore: (selector: (s: unknown) => unknown) => selector({ panel: { mode: "detail", id: panelState.id }, close: vi.fn() }),
}));

import { ReturnShipmentDetailSheet } from "./ReturnShipmentDetailSheet";

const base: ReturnShipment = {
  id: "r1", originalShipmentId: "s1", originalAwb: "KTM-000301", returnAwb: "RTO-20261015-001", rtoReason: "Refused twice",
  originBranchId: "b2", destinationBranchId: "b1", currentStatus: "RtoAtOrigin", currentBranchId: "b1", merchantConfirmedAt: null,
};

beforeEach(() => {
  confirmMutate.mockReset();
  panelState.id = "r1";
  openShipment.mockReset();
  granted = ["rto.manage"];
  returnQuery = { data: base, isLoading: false, isError: false, refetch: vi.fn() };
  shipmentQuery = { data: { awb: "KTM-000301", merchantId: "mer1" } as Shipment, isError: false };
  useShipmentEvents.mockReset();
  useShipmentEvents.mockReturnValue({ data: [{ id: "e1" }, { id: "e2" }], isLoading: false, isError: false, refetch: vi.fn() });
});

describe("ReturnShipmentDetailSheet", () => {
  it("renders the header with reason, back-to, now-at and merchant", () => {
    render(<ReturnShipmentDetailSheet />);
    expect(screen.getByText("RETURN · Refused twice")).toBeInTheDocument();
    expect(screen.getByText("RTO-20261015-001", { selector: "h2" })).toBeInTheDocument();
    expect(screen.getAllByText("Kathmandu Hub")).toHaveLength(2);
    expect(screen.getByText("Daraz Seller")).toBeInTheDocument();
  });

  it("links to the original shipment and tells its story from the original AWB", () => {
    render(<ReturnShipmentDetailSheet />);
    expect(useShipmentEvents).toHaveBeenCalledWith("KTM-000301");
    expect(screen.getByText("timeline with 2 events")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Open original shipment KTM-000301" }));
    expect(openShipment).toHaveBeenCalledWith("KTM-000301");
  });

  it("offers hand-back at origin with handover details as coming soon, behind a confirm", () => {
    render(<ReturnShipmentDetailSheet />);
    expect(screen.getByRole("region", { name: "Handover details" })).toHaveAttribute("data-coming-soon");
    fireEvent.click(screen.getByRole("button", { name: "Confirm handover" }));
    const dialog = screen.getByRole("alertdialog");
    expect(confirmMutate).not.toHaveBeenCalled();
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirm handover" }));
    expect(confirmMutate).toHaveBeenCalledTimes(1);
  });

  it("announces a successful hand-back in a status line, only for the id it was confirmed on", () => {
    confirmMutate.mockImplementation((_: unknown, opts?: { onSuccess?: () => void }) => opts?.onSuccess?.());
    const view = render(<ReturnShipmentDetailSheet />);
    expect(screen.queryByRole("status")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Confirm handover" }));
    fireEvent.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Confirm handover" }));
    expect(screen.getByRole("status")).toHaveTextContent("Handed back to the merchant.");
    panelState.id = "r2";
    returnQuery = { ...returnQuery, data: { ...base, id: "r2" } };
    view.rerender(<IntlTestProvider><ReturnShipmentDetailSheet /></IntlTestProvider>);
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("hides hand-back before origin, without rto.manage, and once confirmed", () => {
    returnQuery = { ...returnQuery, data: { ...base, currentStatus: "RtoInTransit", currentBranchId: null } };
    const first = render(<ReturnShipmentDetailSheet />);
    expect(screen.queryByRole("button", { name: "Confirm handover" })).toBeNull();
    expect(screen.getByText("In transit")).toBeInTheDocument();
    expect(screen.getByText("Hand-back opens once the parcel is at the origin branch.")).toBeInTheDocument();
    first.unmount();

    granted = [];
    returnQuery = { ...returnQuery, data: base };
    const second = render(<ReturnShipmentDetailSheet />);
    expect(screen.queryByRole("button", { name: "Confirm handover" })).toBeNull();
    second.unmount();

    granted = ["rto.manage"];
    returnQuery = { ...returnQuery, data: { ...base, currentStatus: "RtoDelivered", merchantConfirmedAt: "2026-10-15T08:00:00Z" } };
    render(<ReturnShipmentDetailSheet />);
    expect(screen.queryByRole("button", { name: "Confirm handover" })).toBeNull();
    expect(screen.getByText(/^Confirmed by merchant/)).toBeInTheDocument();
  });

  it("shows dashes and an error state when the original's reads fail", () => {
    shipmentQuery = { data: undefined, isError: true };
    useShipmentEvents.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn() });
    render(<ReturnShipmentDetailSheet />);
    expect(screen.getByText("Couldn't load the parcel history")).toBeInTheDocument();
    expect(screen.queryByText("Daraz Seller")).toBeNull();
    expect(screen.getByText("Merchant").parentElement).toHaveTextContent("—");
  });
});
