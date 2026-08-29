import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { RadialMeter } from "./RadialMeter";

describe("RadialMeter", () => {
  it("renders the value as a percentage", () => {
    render(<RadialMeter value={0.7555} colorToken="status-delivered" label="Delivery Success Rate" />);
    expect(screen.getByText("75.5%")).toBeInTheDocument();
    expect(screen.getByText("Delivery Success Rate")).toBeInTheDocument();
  });

  it("clamps a value above 1 to 100%", () => {
    render(<RadialMeter value={1.4} colorToken="status-delivered" label="Rate" />);
    expect(screen.getByText("100.0%")).toBeInTheDocument();
  });

  it("shows an em dash for a null value instead of 0%", () => {
    render(<RadialMeter value={null} colorToken="status-delivered" label="Rate" />);
    expect(screen.getByText("—")).toBeInTheDocument();
  });
});
