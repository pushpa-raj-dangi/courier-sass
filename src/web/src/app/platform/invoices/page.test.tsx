import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";

const invoices = [
  {
    id: "inv-1",
    organizationId: "org-1",
    organizationName: "Kathmandu Couriers",
    planName: "Growth",
    amountNpr: 2500,
    status: "Paid" as const,
    dueAt: "2026-09-01T00:00:00Z",
    paidAt: "2026-09-01T00:00:00Z",
  },
  {
    id: "inv-2",
    organizationId: "org-2",
    organizationName: "Pokhara Logistics",
    planName: "Starter",
    amountNpr: 1000,
    status: "Overdue" as const,
    dueAt: "2026-08-01T00:00:00Z",
    paidAt: null,
  },
];
const pagedResult = { items: invoices, page: 1, pageSize: 25, totalCount: 2, totalPages: 1 };

const usePlatformInvoices = vi.fn(() => ({ data: pagedResult, isLoading: false, isError: false, refetch: vi.fn() }));
vi.mock("@/features/platform-invoices/hooks/usePlatformInvoices", () => ({
  usePlatformInvoices: (...args: unknown[]) => usePlatformInvoices(...(args as [])),
}));

import PlatformInvoicesPage from "./page";

describe("PlatformInvoicesPage", () => {
  it("renders the invoice list with organization names and statuses", () => {
    render(<PlatformInvoicesPage />);

    expect(screen.getByText("Kathmandu Couriers")).toBeInTheDocument();
    expect(screen.getByText("Pokhara Logistics")).toBeInTheDocument();
    expect(screen.getByText("Paid")).toBeInTheDocument();
    expect(screen.getByText("Overdue")).toBeInTheDocument();
  });

  it("passes the debounced search text to the hook after typing", async () => {
    render(<PlatformInvoicesPage />);
    usePlatformInvoices.mockClear();

    fireEvent.change(screen.getByLabelText("Search invoices"), { target: { value: "kathmandu" } });

    await waitFor(() => {
      expect(usePlatformInvoices).toHaveBeenCalledWith("all", "kathmandu", 1, 25);
    }, { timeout: 1000 });
  });

  // Same jsdom+Radix-Select interaction limit already documented for the organizations page —
  // confirms the status filter control renders with the expected default.
  it("renders the status filter defaulted to All statuses", () => {
    render(<PlatformInvoicesPage />);

    expect(screen.getByLabelText("Filter by status")).toBeInTheDocument();
    expect(screen.getByText("All statuses")).toBeInTheDocument();
  });

  it("passes the selected page to the hook via DataTable's onPageChange", async () => {
    usePlatformInvoices.mockReturnValue({
      data: { items: invoices, page: 1, pageSize: 1, totalCount: 2, totalPages: 2 },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    });
    render(<PlatformInvoicesPage />);
    usePlatformInvoices.mockClear();

    fireEvent.click(screen.getByText("2"));

    await waitFor(() => {
      expect(usePlatformInvoices).toHaveBeenCalledWith("all", "", 2, 25);
    });
  });
});
