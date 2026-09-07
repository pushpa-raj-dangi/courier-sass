import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { RouteLineGraphic } from "./RouteLineGraphic";

describe("RouteLineGraphic", () => {
  it("renders as a decorative graphic hidden from assistive tech", () => {
    render(<RouteLineGraphic />);
    expect(screen.getByTestId("route-line-graphic")).toHaveAttribute("aria-hidden", "true");
  });
});
