import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { ShipmentFunnel } from "./ShipmentFunnel";

// Recharts wraps a multi-word y-axis category label across several <tspan> lines (one word per
// line), so its text never sits in one element for testing-library's default text matcher, and
// the tspans' raw textContent has no space between words — join each tick's tspans with a space
// to recover the label.
function axisLabels(container: HTMLElement): string[] {
  return Array.from(container.querySelectorAll(".recharts-cartesian-axis-tick text")).map((node) =>
    Array.from(node.querySelectorAll("tspan"))
      .map((tspan) => tspan.textContent ?? "")
      .join(" ")
  );
}

describe("ShipmentFunnel", () => {
  it("sums multiple raw statuses into one bucket", () => {
    const { container } = render(
      <ShipmentFunnel
        data={{
          AtOriginBranch: 2,
          Sorted: 1,
          InTransit: 4,
          AtDestinationBranch: 0,
          Delivered: 10,
        }}
      />
    );

    const labels = axisLabels(container);
    expect(labels).toContain("In Transit");
    expect(labels).toContain("Delivered");
  });

  it("renders every funnel bucket label, including ones with no matching data", () => {
    const { container } = render(<ShipmentFunnel data={{ Delivered: 3 }} />);

    const labels = axisLabels(container);
    expect(labels).toEqual([
      "Created",
      "Picked Up",
      "In Transit",
      "Out for Delivery",
      "Delivered",
      "NDR",
      "RTO",
      "Cancelled",
    ]);
  });
});
