import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";

const records = [
  {
    id: "log-1",
    organizationId: "org-1",
    organizationName: "Kathmandu Couriers",
    action: "shipment.created",
    entityType: "Shipment",
    entityId: "ship-1",
    userId: "user-1",
    impersonatedByUserId: null,
    isCrossTenantAction: false,
    timestamp: "2026-09-01T10:00:00Z",
    before: null,
    after: null,
    ipAddress: null,
    userAgent: null,
  },
];
const pagedResult = { items: records, page: 1, pageSize: 25, totalCount: 1, totalPages: 1 };

const usePlatformAuditLogs = vi.fn(() => ({ data: pagedResult, isLoading: false, isError: false, refetch: vi.fn() }));
vi.mock("@/features/platform-audit-logs/hooks/usePlatformAuditLogs", () => ({
  usePlatformAuditLogs: (...args: unknown[]) => usePlatformAuditLogs(...(args as [])),
}));

import PlatformAuditLogsPage from "./page";

describe("PlatformAuditLogsPage", () => {
  it("renders the audit log list with the organization name column", () => {
    render(<PlatformAuditLogsPage />);

    expect(screen.getByText("shipment.created")).toBeInTheDocument();
    expect(screen.getByText("Kathmandu Couriers")).toBeInTheDocument();
  });

  it("passes the debounced organization search to the hook after typing", async () => {
    render(<PlatformAuditLogsPage />);
    usePlatformAuditLogs.mockClear();

    fireEvent.change(screen.getByLabelText("Search by organization name"), { target: { value: "kathmandu" } });

    await waitFor(() => {
      expect(usePlatformAuditLogs).toHaveBeenCalledWith("kathmandu", "", "", "", "", 1, 25);
    }, { timeout: 1000 });
  });
});
