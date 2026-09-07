import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { HowItWorks } from "./HowItWorks";
import { HOW_IT_WORKS_STEPS } from "../content";

describe("HowItWorks", () => {
  it("renders all 4 steps as an ordered list of 4 items", () => {
    render(<HowItWorks />);
    expect(screen.getAllByRole("listitem")).toHaveLength(4);
  });

  it("renders every step's title and description", () => {
    render(<HowItWorks />);
    for (const step of HOW_IT_WORKS_STEPS) {
      expect(screen.getByText(step.title)).toBeInTheDocument();
      expect(screen.getByText(step.description)).toBeInTheDocument();
    }
  });

  it("has the #how-it-works anchor id for nav scrolling", () => {
    const { container } = render(<HowItWorks />);
    expect(container.querySelector("#how-it-works")).not.toBeNull();
  });

  it("renders the decorative route-line graphic", () => {
    render(<HowItWorks />);
    expect(screen.getByTestId("route-line-graphic")).toBeInTheDocument();
  });
});
