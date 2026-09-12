import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";

const records = [
  {
    id: "log-1",
    organizationId: "org-1",
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
  {
    id: "log-2",
    organizationId: "org-1",
    action: "branch.created",
    entityType: "Branch",
    entityId: "branch-1",
    userId: "user-1",
    impersonatedByUserId: null,
    isCrossTenantAction: false,
    timestamp: "2026-08-30T09:00:00Z",
    before: null,
    after: null,
    ipAddress: null,
    userAgent: null,
  },
];
const pagedResult = { items: records, page: 1, pageSize: 25, totalCount: 2, totalPages: 1 };

const useAuditLogs = vi.fn(() => ({ data: pagedResult, isLoading: false, isError: false, refetch: vi.fn() }));
vi.mock("@/features/audit-logs/hooks/useAuditLogs", () => ({
  useAuditLogs: (...args: unknown[]) => useAuditLogs(...(args as [])),
}));

import AuditLogsPage from "./page";

describe("AuditLogsPage", () => {
  beforeEach(() => {
    useAuditLogs.mockClear();
  });

  it("renders the audit log list with action and entity columns", () => {
    render(<AuditLogsPage />);

    expect(screen.getByText("shipment.created")).toBeInTheDocument();
    expect(screen.getByText("branch.created")).toBeInTheDocument();
  });

  it("passes the debounced search text to the hook after typing", async () => {
    render(<AuditLogsPage />);
    useAuditLogs.mockClear();

    fireEvent.change(screen.getByLabelText("Search audit logs"), { target: { value: "shipment" } });

    await waitFor(() => {
      expect(useAuditLogs).toHaveBeenCalledWith("shipment", "", "", "", 1, 25);
    }, { timeout: 1000 });
  });

  it("passes the entity type filter to the hook", async () => {
    render(<AuditLogsPage />);
    useAuditLogs.mockClear();

    fireEvent.change(screen.getByLabelText("Filter by entity type"), { target: { value: "Branch" } });

    await waitFor(() => {
      expect(useAuditLogs).toHaveBeenCalledWith("", "Branch", "", "", 1, 25);
    }, { timeout: 1000 });
  });

  it("passes the selected page to the hook via DataTable's onPageChange", async () => {
    useAuditLogs.mockReturnValue({
      data: { items: records, page: 1, pageSize: 1, totalCount: 2, totalPages: 2 },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    });
    render(<AuditLogsPage />);
    useAuditLogs.mockClear();

    fireEvent.click(screen.getByText("2"));

    await waitFor(() => {
      expect(useAuditLogs).toHaveBeenCalledWith("", "", "", "", 2, 25);
    });
  });
});
