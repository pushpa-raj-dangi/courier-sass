import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { BranchExceptions } from "./BranchExceptions";

describe("BranchExceptions", () => {
  it("aggregates the riders array into a count and total open tasks, not a named list", () => {
    render(
      <BranchExceptions
        data={{
          missingShipmentExceptions: 2,
          openNdrCount: 5,
          riders: [
            { riderId: "r1", openTaskCount: 3 },
            { riderId: "r2", openTaskCount: 4 },
          ],
          codPendingFinanceVerification: 1500,
        }}
      />
    );

    expect(screen.getByText("2 riders")).toBeInTheDocument();
    expect(screen.getByText("7 open tasks")).toBeInTheDocument();
    expect(screen.queryByText("r1")).not.toBeInTheDocument();
  });
});
