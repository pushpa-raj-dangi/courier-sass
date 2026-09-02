import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { QuickStatsStrip } from "./QuickStatsStrip";
import type { OrgDashboardSummary } from "@/features/reports/types";

const baseData: OrgDashboardSummary = {
  todaysShipmentsByStatus: { Created: 4, PickedUp: 2, Delivered: 10, NdrCreated: 1 },
  financial: { expected: 50000, collected: 30000, pending: 20000, settled: 15000, merchantPayable: 12000 },
  branchPerformance: {
    "Kathmandu Hub": { shipmentVolume: 20, deliveryAttempts: 18, deliverySuccesses: 16, deliverySuccessRate: 0.89, ndrCount: 2, ndrRate: 0.11, rtoCount: 0, rtoRate: 0 },
    "Pokhara Hub": { shipmentVolume: 10, deliveryAttempts: 8, deliverySuccesses: 7, deliverySuccessRate: 0.88, ndrCount: 1, ndrRate: 0.13, rtoCount: 0, rtoRate: 0 },
  },
  riderPerformance: [],
  overallDeliverySuccessRate: 0.9,
  overallNdrRate: 0.05,
  overallRtoRate: 0.01,
  averageDeliveryTime: "02:15:30",
};

describe("QuickStatsStrip", () => {
  it("sums today's shipments across every status", () => {
    render(<QuickStatsStrip data={baseData} />);
    expect(screen.getByText("17")).toBeInTheDocument();
  });

  it("reads Delivered directly out of today's status breakdown", () => {
    render(<QuickStatsStrip data={baseData} />);
    expect(screen.getByText("10")).toBeInTheDocument();
  });

  it("falls back to zero delivered today when Delivered is absent from the breakdown", () => {
    render(<QuickStatsStrip data={{ ...baseData, todaysShipmentsByStatus: { Created: 3 } }} />);
    const label = screen.getByText("Delivered Today");
    const value = label.nextElementSibling;
    expect(value).toHaveTextContent("0");
  });

  it("counts distinct branch keys as active branches", () => {
    render(<QuickStatsStrip data={baseData} />);
    expect(screen.getByText("2")).toBeInTheDocument();
  });

  it("formats COD pending as compact NPR currency", () => {
    render(<QuickStatsStrip data={baseData} />);
    expect(screen.getByText(/NPR/)).toBeInTheDocument();
  });
});
