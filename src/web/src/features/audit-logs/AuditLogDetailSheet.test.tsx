import { describe, expect, it, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { AuditLogDetailSheet } from "./AuditLogDetailSheet";
import { useAuditLogPanelStore } from "./store/auditLogPanelStore";
import type { AuditLogRecord } from "./types";

const record: AuditLogRecord = {
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
  after: "{\"status\":\"Created\"}",
  ipAddress: "203.0.113.1",
  userAgent: "Mozilla/5.0",
};

describe("AuditLogDetailSheet", () => {
  beforeEach(() => {
    useAuditLogPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("renders nothing open when the panel is closed", () => {
    render(<AuditLogDetailSheet />);

    expect(screen.queryByText("shipment.created")).not.toBeInTheDocument();
  });

  it("shows the full record when a row is opened", () => {
    useAuditLogPanelStore.getState().openDetail(record);
    render(<AuditLogDetailSheet />);

    expect(screen.getByText("shipment.created")).toBeInTheDocument();
    expect(screen.getByText("Shipment")).toBeInTheDocument();
    expect(screen.getByText("203.0.113.1")).toBeInTheDocument();
    expect(screen.getByText("{\"status\":\"Created\"}")).toBeInTheDocument();
  });
});
