import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { Credibility } from "./Credibility";
import { CREDIBILITY_POINTS } from "../content";

describe("Credibility", () => {
  it("renders every credibility point's title and description", () => {
    render(<Credibility />);
    for (const point of CREDIBILITY_POINTS) {
      expect(screen.getByText(point.title)).toBeInTheDocument();
      expect(screen.getByText(point.description)).toBeInTheDocument();
    }
  });

  it("explicitly states there are no case studies yet, rather than fabricating any", () => {
    render(<Credibility />);
    expect(screen.getByText(/no case studies yet/i)).toBeInTheDocument();
  });
});
