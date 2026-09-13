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
    entityName: null,
    userId: "user-1",
    actorName: "Ramesh Shrestha",
    impersonatedByUserId: null,
    isCrossTenantAction: false,
    timestamp: "2026-09-01T10:00:00Z",
    before: null,
    after: null,
    ipAddress: null,
    userAgent: null,
  },
  {
    id: "log-2",
    organizationId: "org-1",
    organizationName: "Kathmandu Couriers",
    action: "organization.suspended",
    entityType: "Organization",
    entityId: "org-1",
    entityName: "Kathmandu Couriers",
    userId: "user-1",
    actorName: null,
    impersonatedByUserId: null,
    isCrossTenantAction: true,
    timestamp: "2026-09-02T10:00:00Z",
    before: null,
    after: null,
    ipAddress: null,
    userAgent: null,
  },
];
const pagedResult = { items: records, page: 1, pageSize: 25, totalCount: 2, totalPages: 1 };

const usePlatformAuditLogs = vi.fn(() => ({ data: pagedResult, isLoading: false, isError: false, refetch: vi.fn() }));
vi.mock("@/features/platform-audit-logs/hooks/usePlatformAuditLogs", () => ({
  usePlatformAuditLogs: (...args: unknown[]) => usePlatformAuditLogs(...(args as [])),
}));

import PlatformAuditLogsPage from "./page";

describe("PlatformAuditLogsPage", () => {
  it("renders the audit log list with the organization name column", () => {
    render(<PlatformAuditLogsPage />);

    expect(screen.getByText("shipment.created")).toBeInTheDocument();
    expect(screen.getAllByText("Kathmandu Couriers").length).toBeGreaterThan(0);
  });

  it("shows the actor's name instead of their raw id when one was resolved", () => {
    render(<PlatformAuditLogsPage />);

    expect(screen.getByText("Ramesh Shrestha")).toBeInTheDocument();
    const rows = screen.getAllByRole("row");
    const createdRow = rows.find((row) => row.textContent?.includes("shipment.created"));
    expect(createdRow?.textContent).not.toContain("user-1");
  });

  it("falls back to the raw actor id when no name could be resolved", () => {
    render(<PlatformAuditLogsPage />);

    // log-2's actorName is null — its row must still show something usable, not blank.
    const rows = screen.getAllByRole("row");
    const suspendedRow = rows.find((row) => row.textContent?.includes("organization.suspended"));
    expect(suspendedRow?.textContent).toContain("user-1");
  });

  it("shows the resolved entity name for a row whose entity is known, and falls back to the id otherwise", () => {
    render(<PlatformAuditLogsPage />);

    expect(screen.getByText("Organization: Kathmandu Couriers")).toBeInTheDocument();
    expect(screen.getByText("Shipment (ship-1)")).toBeInTheDocument();
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
