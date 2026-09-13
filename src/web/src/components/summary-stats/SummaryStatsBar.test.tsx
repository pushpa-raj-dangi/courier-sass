import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { Package } from "lucide-react";
import { SummaryStatsBar, type StatTile } from "./SummaryStatsBar";

const tiles: StatTile[] = [
  { label: "Created", value: "12", icon: Package, badgeClass: "bg-status-created/15 text-status-created" },
  { label: "Delivered", value: "40", icon: Package, badgeClass: "bg-status-delivered/15 text-status-delivered", highlight: true },
];

describe("SummaryStatsBar", () => {
  it("renders a tile per entry with its label and value", () => {
    render(<SummaryStatsBar tiles={tiles} isLoading={false} isError={false} />);
    expect(screen.getByText("Created")).toBeInTheDocument();
    expect(screen.getByText("12")).toBeInTheDocument();
    expect(screen.getByText("Delivered")).toBeInTheDocument();
    expect(screen.getByText("40")).toBeInTheDocument();
  });

  it("renders skeleton placeholders while loading instead of tiles", () => {
    render(<SummaryStatsBar tiles={[]} isLoading={true} isError={false} skeletonCount={3} />);
    expect(screen.queryByText("Created")).not.toBeInTheDocument();
    expect(screen.getAllByTestId("summary-stat-skeleton")).toHaveLength(3);
  });

  it("renders nothing but does not throw when errored", () => {
    const { container } = render(<SummaryStatsBar tiles={[]} isLoading={false} isError={true} />);
    expect(container.querySelector('[data-testid="summary-stats-error"]')).toBeInTheDocument();
  });
});
