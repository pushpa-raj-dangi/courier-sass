import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { ScreenShowcase } from "./ScreenShowcase";
import { SCREENSHOTS } from "../content";

describe("ScreenShowcase", () => {
  it("renders every screenshot's image and caption", () => {
    render(<ScreenShowcase />);
    for (const screenshot of SCREENSHOTS) {
      expect(screen.getByRole("img", { name: screenshot.alt })).toBeInTheDocument();
      expect(screen.getByText(screenshot.caption)).toBeInTheDocument();
    }
  });

  it("renders a section heading", () => {
    render(<ScreenShowcase />);
    expect(screen.getByRole("heading", { level: 2 })).toBeInTheDocument();
  });
});
