import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { CodWaterfallDiagram } from "./CodWaterfallDiagram";

describe("CodWaterfallDiagram", () => {
  it("shows collected, fee and merchant payable as text (2,500 − 150 = 2,350)", () => {
    render(<CodWaterfallDiagram />);
    const figure = screen.getByRole("figure", { name: /cod/i });
    expect(figure).toHaveTextContent("NPR 2,500");
    expect(figure).toHaveTextContent("NPR 150");
    expect(figure).toHaveTextContent("NPR 2,350");
  });
  it("derives payable from props rather than hardcoding it", () => {
    render(<CodWaterfallDiagram collected={1000} fee={100} />);
    expect(screen.getByRole("figure", { name: /cod/i })).toHaveTextContent("NPR 900");
  });
  it("discloses sample data", () => {
    render(<CodWaterfallDiagram />);
    expect(screen.getByText(/sample data/i)).toBeInTheDocument();
  });
});
