import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BranchRecord } from "./BranchRecord";
import type { Branch } from "./types";

const branch: Branch = {
  id: "b1",
  code: "KTM-01",
  name: "Kathmandu Hub",
  type: "Hub",
  provinceId: "p1",
  districtId: "d1",
  localLevelId: "l1",
  wardNumber: 5,
  addressLine: "New Road",
  phone: "9800000000",
  operatingHours: "9:00 AM - 6:00 PM",
  latitude: null,
  longitude: null,
  parentBranchId: null,
  deliveryZoneId: null,
  isActive: true,
};

function renderRecord() {
  global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => [] });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <BranchRecord branch={branch} />
    </QueryClientProvider>
  );
}

describe("BranchRecord", () => {
  it("renders every branch field read-only", () => {
    renderRecord();

    expect(screen.getByText("KTM-01")).toBeInTheDocument();
    expect(screen.getByText("Kathmandu Hub")).toBeInTheDocument();
    expect(screen.getByText("New Road")).toBeInTheDocument();
    // No text input exists for Name/Code/Address - everything but delivery zone is plain text.
    expect(screen.queryByLabelText(/^name$/i)).not.toBeInTheDocument();
  });
});
