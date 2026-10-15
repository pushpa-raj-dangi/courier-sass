import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { DeliveryAttempt, Ndr } from "@/features/ndr/types";
import type { Shipment } from "@/features/shipments/types";

let ndrQuery: { data?: Ndr; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/ndr/hooks/useNdr", () => ({ useNdr: () => ndrQuery }));
let shipmentQuery: { data?: Shipment; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/shipments/hooks/useShipment", () => ({ useShipment: () => shipmentQuery }));
vi.mock("@/features/shipments/hooks/useMerchants", () => ({ useMerchants: () => ({ data: [{ id: "mer1", displayName: "Daraz Seller", isActive: true }] }) }));
let attemptsQuery: { data?: { items: DeliveryAttempt[] }; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/ndr/hooks/useDeliveryAttempts", () => ({ useDeliveryAttempts: () => attemptsQuery }));
const updateMutateAsync = vi.fn();
vi.mock("@/features/ndr/hooks/useUpdateNdr", () => ({ useUpdateNdr: () => ({ mutateAsync: updateMutateAsync, isPending: false }) }));
const resolveMutateAsync = vi.fn();
vi.mock("@/features/ndr/hooks/useResolveNdr", () => ({ useResolveNdr: () => ({ mutateAsync: resolveMutateAsync, isPending: false }) }));
vi.mock("@/features/ndr/hooks/useReturnShipments", () => ({ useReturnShipments: () => ({ data: [] }) }));
vi.mock("@/features/ndr/store/returnShipmentPanelStore", () => ({
  useReturnShipmentPanelStore: (selector: (s: unknown) => unknown) => selector({ openDetail: vi.fn() }),
}));
vi.mock("@/features/auth/hooks/useSession", () => ({ useSession: () => ({ data: { userId: "u1" } }) }));
let granted: string[] = [];
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => granted.includes(p) }));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ riderName: () => "Hari Thapa", userName: () => "Sita Rai", branchName: () => "Branch" }),
}));
vi.mock("@/features/ndr/store/ndrPanelStore", () => ({
  useNdrPanelStore: (selector: (s: unknown) => unknown) => selector({ panel: { mode: "detail", id: "n1" }, close: vi.fn() }),
}));

import { NdrDetailSheet } from "./NdrDetailSheet";

const baseNdr: Ndr = {
  id: "n1", shipmentId: "s1", awb: "KTM-000301", reason: "CustomerUnavailable", assignedStaffUserId: "u1", status: "Open",
  customerResponse: null, followUpAt: null, nextAction: null, resolution: null, resolvedAt: null, createdAt: "2026-10-14T06:00:00Z",
};

beforeEach(() => {
  updateMutateAsync.mockReset().mockResolvedValue({});
  resolveMutateAsync.mockReset().mockResolvedValue({});
  granted = ["ndr.manage", "ndr.resolve"];
  ndrQuery = { data: baseNdr, isLoading: false, isError: false, refetch: vi.fn() };
  shipmentQuery = {
    data: { awb: "KTM-000301", recipientName: "Ram Shrestha", merchantId: "mer1", codAmount: 2500 } as Shipment,
    isLoading: false, isError: false, refetch: vi.fn(),
  };
  attemptsQuery = {
    data: { items: [
      { attemptNumber: 2, attemptedAt: "2026-10-14T05:00:00Z", succeeded: false, reason: "CustomerUnavailable", notes: "Gate locked", customerContacted: true, nextAction: "Reattempt", riderId: "r1" },
      { attemptNumber: 1, attemptedAt: "2026-10-13T05:00:00Z", succeeded: false, reason: "PhoneUnreachable", notes: null, customerContacted: false, nextAction: null, riderId: "r1" },
    ] },
    isLoading: false, isError: false, refetch: vi.fn(),
  };
});

describe("NdrDetailSheet", () => {
  it("shows the eyebrow, parcel facts from the shipment read and the attempt count", () => {
    render(<NdrDetailSheet />);
    expect(screen.getByText(/^NDR · Opened/)).toBeInTheDocument();
    expect(screen.getByText("Ram Shrestha")).toBeInTheDocument();
    expect(screen.getByText("Daraz Seller")).toBeInTheDocument();
    expect(screen.getByText("Rs 2,500")).toBeInTheDocument();
    expect(screen.getByText("Owner: you")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Assign to…" })).toBeDisabled();
  });

  it("falls back to dashes with a retry when the shipment read fails", () => {
    shipmentQuery = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    render(<NdrDetailSheet />);
    expect(screen.getByText("Parcel details are unavailable.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(shipmentQuery.refetch).toHaveBeenCalled();
  });

  it("lists attempts newest first with reason, rider and notes", () => {
    render(<NdrDetailSheet />);
    const attempts = screen.getByRole("list", { name: "Delivery attempts" });
    const rows = within(attempts).getAllByRole("listitem");
    expect(rows[0]).toHaveTextContent("Attempt 2");
    expect(rows[0]).toHaveTextContent("Customer unavailable · customer contacted");
    expect(rows[0]).toHaveTextContent("Hari Thapa");
    expect(rows[0]).toHaveTextContent("Gate locked");
    expect(rows[1]).toHaveTextContent("Phone unreachable");
  });

  it("gives the attempts card its own empty and error states", () => {
    attemptsQuery = { data: { items: [] }, isLoading: false, isError: false, refetch: vi.fn() };
    const { unmount } = render(<NdrDetailSheet />);
    expect(screen.getByText("No delivery attempts recorded.")).toBeInTheDocument();
    unmount();
    attemptsQuery = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    render(<NdrDetailSheet />);
    expect(screen.getByText("Couldn't load delivery attempts")).toBeInTheDocument();
  });

  it("submits the follow-up with the only legal next status", async () => {
    const user = userEvent.setup();
    render(<NdrDetailSheet />);
    await user.type(screen.getByLabelText("Customer response"), "Home after 5");
    await user.click(screen.getByRole("button", { name: "Save follow-up" }));
    await waitFor(() =>
      expect(updateMutateAsync).toHaveBeenCalledWith({ status: "Contacting", customerResponse: "Home after 5", nextAction: undefined, followUpAt: undefined }),
    );
    expect(await screen.findByText("Follow-up saved.")).toBeInTheDocument();
  });

  it("requires a next status when the NDR can branch", async () => {
    const user = userEvent.setup();
    ndrQuery = { ...ndrQuery, data: { ...baseNdr, status: "CustomerConfirmed" } };
    render(<NdrDetailSheet />);
    await user.click(screen.getByRole("button", { name: "Save follow-up" }));
    expect(await screen.findByText("Choose the next status.")).toBeInTheDocument();
    expect(updateMutateAsync).not.toHaveBeenCalled();
  });

  it("disables reattempt and return for an Open NDR and confirms 'Resolve other'", async () => {
    render(<NdrDetailSheet />);
    expect(screen.getByRole("radio", { name: /Reattempt/ })).toBeDisabled();
    expect(screen.getByRole("radio", { name: /Return to origin/ })).toBeDisabled();
    expect(screen.getByText("Available once the NDR is RTO requested.")).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: /Resolve other/ })).toBeChecked();
    fireEvent.click(screen.getByRole("button", { name: "Resolve" }));
    const dialog = screen.getByRole("alertdialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Resolve" }));
    await waitFor(() => expect(resolveMutateAsync).toHaveBeenCalledWith({ resolution: "Other", rtoReason: undefined }));
  });

  it("requires a reason before starting a return, then confirms", async () => {
    const user = userEvent.setup();
    ndrQuery = { ...ndrQuery, data: { ...baseNdr, status: "RtoRequested" } };
    render(<NdrDetailSheet />);
    expect(screen.getByRole("radio", { name: /Return to origin/ })).toBeChecked();
    await user.click(screen.getByRole("button", { name: "Start return" }));
    expect(screen.getByText("Give a reason for the return.")).toBeInTheDocument();
    expect(resolveMutateAsync).not.toHaveBeenCalled();

    await user.type(screen.getByLabelText("Reason for the return"), "Refused twice");
    await user.click(screen.getByRole("button", { name: "Start return" }));
    await user.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Start return" }));
    await waitFor(() => expect(resolveMutateAsync).toHaveBeenCalledWith({ resolution: "Rto", rtoReason: "Refused twice" }));
  });

  it("schedules a reattempt without a dialog and explains the missing date", async () => {
    ndrQuery = { ...ndrQuery, data: { ...baseNdr, status: "ReattemptScheduled", followUpAt: "2026-10-16T04:00:00Z" } };
    render(<NdrDetailSheet />);
    expect(screen.queryByRole("button", { name: "Save follow-up" })).toBeNull();
    expect(screen.getByText(/booking a dated reattempt needs backend support/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Schedule reattempt" }));
    await waitFor(() => expect(resolveMutateAsync).toHaveBeenCalledWith({ resolution: "Reattempt", rtoReason: undefined }));
  });

  it("is read-only without ndr.manage and hides resolve without ndr.resolve", () => {
    granted = [];
    render(<NdrDetailSheet />);
    expect(screen.getByText("You can view this follow-up but not change it.")).toBeInTheDocument();
    expect(screen.queryByRole("radio")).toBeNull();
  });

  it("shows the resolved summary instead of both forms", () => {
    ndrQuery = { ...ndrQuery, data: { ...baseNdr, status: "Resolved", resolution: "Other", resolvedAt: "2026-10-15T00:00:00Z" } };
    render(<NdrDetailSheet />);
    expect(screen.getByText(/Resolved as/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Save follow-up" })).toBeNull();
    expect(screen.queryByRole("radio")).toBeNull();
  });
});
