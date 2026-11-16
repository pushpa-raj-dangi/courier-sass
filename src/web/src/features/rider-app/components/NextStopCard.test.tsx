import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";
import { QueryClient, QueryClientProvider, useMutation } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { MyTodayTask } from "@/features/riders/types";
import { useHandledStore } from "@/features/rider-app/store/handledStore";

const pickupFn = vi.fn();
// A real mutation so isPending/isSuccess move exactly as they do on the phone.
vi.mock("@/features/rider-app/hooks/useRiderPickupScan", () => ({
  useRiderPickupScan: () => useMutation({ mutationFn: (vars: { awb: string; shipmentId: string }) => pickupFn(vars) }),
}));
const refresh = vi.fn();
vi.mock("@/features/rider-app/sync/outboxStatusStore", () => ({ useOutboxStatusStore: { getState: () => ({ refresh }) } }));

import { NextStopCard } from "./NextStopCard";

const pickup: MyTodayTask = {
  taskId: "t3", shipmentId: "s3", type: "Pickup", sequence: 3, awb: "NX-260930-00152", recipientName: "Hari Traders",
  recipientPhone: "9800000412", recipientAddressLine: "Kupondole", recipientWardNumber: 4, codAmount: 0, status: "Pending",
};

function wrap(ui: ReactNode) {
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  return <QueryClientProvider client={client}>{ui}</QueryClientProvider>;
}

beforeEach(() => {
  pickupFn.mockReset();
  refresh.mockReset();
  useHandledStore.setState({ handled: new Set() });
});

describe("NextStopCard", () => {
  it("can't scan the same pickup twice once the scan is saved", async () => {
    pickupFn.mockResolvedValue(undefined);
    render(wrap(<NextStopCard task={pickup} riderId="r1" syncState="synced" />));
    const button = screen.getByRole("button", { name: "Scan pickup" });
    fireEvent.click(button);
    await waitFor(() => expect(refresh).toHaveBeenCalled());
    expect(useHandledStore.getState().handled.has("s3")).toBe(true);
    expect(screen.getByRole("button", { name: "Scan pickup" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Scan pickup" }));
    expect(pickupFn).toHaveBeenCalledTimes(1);
  });

  it("lets the rider try again when the scan couldn't be saved, and says so", async () => {
    pickupFn.mockRejectedValue(new Error("idb"));
    render(wrap(<NextStopCard task={pickup} riderId="r1" syncState="synced" />));
    fireEvent.click(screen.getByRole("button", { name: "Scan pickup" }));
    expect(await screen.findByText("Couldn't save this scan · try again")).toBeInTheDocument();
    expect(useHandledStore.getState().handled.has("s3")).toBe(false);
    expect(screen.getByRole("button", { name: "Scan pickup" })).toBeEnabled();
  });
});
