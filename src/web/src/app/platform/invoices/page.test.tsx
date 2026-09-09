import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

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

vi.mock("@/features/platform-invoices/hooks/usePlatformInvoices", () => ({
  usePlatformInvoices: () => ({ data: invoices, isLoading: false, isError: false, refetch: vi.fn() }),
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
});
