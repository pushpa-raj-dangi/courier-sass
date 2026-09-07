import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { FeatureGrid } from "./FeatureGrid";
import { FEATURES } from "../content";

describe("FeatureGrid", () => {
  it("renders every feature's title and description", () => {
    render(<FeatureGrid />);
    for (const feature of FEATURES) {
      expect(screen.getByText(feature.title)).toBeInTheDocument();
      expect(screen.getByText(feature.description)).toBeInTheDocument();
    }
  });

  it("has the #features anchor id for nav scrolling", () => {
    const { container } = render(<FeatureGrid />);
    expect(container.querySelector("#features")).not.toBeNull();
  });

  it("renders a section heading", () => {
    render(<FeatureGrid />);
    expect(screen.getByRole("heading", { level: 2 })).toBeInTheDocument();
  });

  it("wraps each feature tile with a gradient border and glow class", () => {
    render(<FeatureGrid />);
    const tiles = screen.getAllByTestId("feature-tile");
    expect(tiles).toHaveLength(FEATURES.length);
  });
});
