import { describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithIntl } from "@/test/renderWithIntl";
import { ComingSoonSlot } from "./ComingSoonSlot";

describe("ComingSoonSlot", () => {
  it("renders a labelled region with title, description and the needs-backend caption", () => {
    renderWithIntl(<ComingSoonSlot title="Hourly flow" description="Booked vs delivered per hour." />);

    const region = screen.getByRole("region", { name: "Hourly flow" });
    expect(region).toHaveAttribute("data-coming-soon");
    expect(screen.getByText("Booked vs delivered per hour.")).toBeInTheDocument();
    expect(screen.getByText("Needs backend")).toBeInTheDocument();
  });

  it("applies minHeight when given", () => {
    renderWithIntl(<ComingSoonSlot title="Bins" description="Sort bins." minHeight={240} />);
    expect(screen.getByRole("region", { name: "Bins" })).toHaveStyle({ minHeight: "240px" });
  });

  it("renders the caption in Nepali", () => {
    renderWithIntl(<ComingSoonSlot title="Bins" description="Sort bins." />, { locale: "ne" });
    expect(screen.getByText("ब्याकइन्ड आवश्यक")).toBeInTheDocument();
  });
});
