import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { PickupScanControl } from "./PickupScanControl";
import * as clientFetchModule from "@/lib/api/clientFetch";

function renderWithProviders(riderId: string, awb: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <PickupScanControl riderId={riderId} awb={awb} />
    </QueryClientProvider>
  );
}

describe("PickupScanControl", () => {
  it("submits the AWB and shows a confirmation on success", async () => {
    const user = userEvent.setup();
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue({ id: "s1", currentStatus: "PickedUp" });
    renderWithProviders("r1", "AWB001");

    await user.click(screen.getByRole("button", { name: /scan pickup/i }));

    await waitFor(() => expect(screen.getByText(/picked up/i)).toBeInTheDocument());
    expect(clientFetchModule.clientFetch).toHaveBeenCalledWith(
      "/api/riders/r1/scan/pickup",
      expect.objectContaining({ method: "POST" })
    );
  });
});
