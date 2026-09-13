import { describe, expect, it, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { PlatformAuditLogDetailSheet } from "./PlatformAuditLogDetailSheet";
import { usePlatformAuditLogPanelStore } from "./store/platformAuditLogPanelStore";
import type { AuditLogRecord } from "./types";

const record: AuditLogRecord = {
  id: "log-1",
  organizationId: "org-1",
  organizationName: "Kathmandu Couriers",
  action: "organization.suspended",
  entityType: "Organization",
  entityId: "org-1",
  entityName: "Kathmandu Couriers",
  userId: null,
  actorName: null,
  impersonatedByUserId: null,
  isCrossTenantAction: true,
  timestamp: "2026-09-01T10:00:00Z",
  before: null,
  after: null,
  ipAddress: "203.0.113.1",
  userAgent: null,
};

describe("PlatformAuditLogDetailSheet", () => {
  beforeEach(() => {
    usePlatformAuditLogPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("shows the organization name alongside the record when opened", () => {
    usePlatformAuditLogPanelStore.getState().openDetail(record);
    render(<PlatformAuditLogDetailSheet />);

    expect(screen.getByText("organization.suspended")).toBeInTheDocument();
    expect(screen.getAllByText("Kathmandu Couriers").length).toBeGreaterThan(0);
  });

  it("shows 'System' for a null actor, and the resolved entity name instead of the raw entity id", () => {
    usePlatformAuditLogPanelStore.getState().openDetail(record);
    render(<PlatformAuditLogDetailSheet />);

    expect(screen.getByText("System")).toBeInTheDocument();
    expect(screen.queryByText("org-1")).not.toBeInTheDocument();
  });

  it("shows the actor's resolved name instead of their raw id", () => {
    usePlatformAuditLogPanelStore.getState().openDetail({ ...record, userId: "user-9", actorName: "Sita Gurung" });
    render(<PlatformAuditLogDetailSheet />);

    expect(screen.getByText("Sita Gurung")).toBeInTheDocument();
    expect(screen.queryByText("user-9")).not.toBeInTheDocument();
  });
});
