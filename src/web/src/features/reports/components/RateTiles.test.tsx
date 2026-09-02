import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { RateTiles } from "./RateTiles";

const data = { overallNdrRate: 0.05, overallRtoRate: 0.02, averageDeliveryTime: "02:15:30" };

describe("RateTiles", () => {
  it("defaults the range caption to Last 30 days when none is provided", () => {
    render(<RateTiles data={data} />);
    expect(screen.getByText("Last 30 days")).toBeInTheDocument();
  });

  it("shows a custom range caption when provided", () => {
    render(<RateTiles data={data} rangeLabel="2026-08-01 – 2026-08-31" />);
    expect(screen.getByText("2026-08-01 – 2026-08-31")).toBeInTheDocument();
  });
});
