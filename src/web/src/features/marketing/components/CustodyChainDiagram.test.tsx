import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { CustodyChainDiagram } from "./CustodyChainDiagram";
import { CUSTODY_CHAIN } from "../content";

describe("CustodyChainDiagram", () => {
  it("is a labelled figure exposing every step as text for assistive tech", () => {
    render(<CustodyChainDiagram />);
    const figure = screen.getByRole("figure", { name: /custody chain/i });
    expect(figure).toBeInTheDocument();
    for (const step of CUSTODY_CHAIN) {
      expect(screen.getAllByText(step.label).length).toBeGreaterThan(0);
    }
  });
  it("keeps the decorative SVGs out of the accessibility tree", () => {
    const { container } = render(<CustodyChainDiagram />);
    for (const svg of container.querySelectorAll("svg")) {
      expect(svg).toHaveAttribute("aria-hidden", "true");
    }
  });
  it("explains the node legend", () => {
    render(<CustodyChainDiagram />);
    expect(screen.getByText(/scan-verified/i)).toBeInTheDocument();
  });
});
