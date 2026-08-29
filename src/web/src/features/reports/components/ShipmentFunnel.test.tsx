import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { ShipmentFunnel } from "./ShipmentFunnel";

describe("ShipmentFunnel", () => {
  it("sums multiple raw statuses into one bucket", () => {
    render(
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

    const inTransitRow = screen.getByText("In Transit").closest('[tabindex="0"]');
    expect(inTransitRow).toHaveTextContent("7");
    expect(screen.getByText("10")).toBeInTheDocument();
  });

  it("renders 0 for a bucket with no matching statuses in the data", () => {
    render(<ShipmentFunnel data={{ Delivered: 3 }} />);
    const createdRow = screen.getByText("Created").closest('[tabindex="0"]');
    expect(createdRow).toHaveTextContent("0");
  });
});
