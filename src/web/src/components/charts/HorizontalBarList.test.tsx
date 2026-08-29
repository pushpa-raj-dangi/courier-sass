import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { HorizontalBarList } from "./HorizontalBarList";

describe("HorizontalBarList", () => {
  it("renders each row's label and value", () => {
    render(
      <HorizontalBarList
        rows={[
          { key: "a", label: "Alpha", value: 10, color: "red" },
          { key: "b", label: "Beta", value: 5, color: "blue" },
        ]}
      />
    );

    expect(screen.getByText("Alpha")).toBeInTheDocument();
    expect(screen.getByText("10")).toBeInTheDocument();
    expect(screen.getByText("Beta")).toBeInTheDocument();
    expect(screen.getByText("5")).toBeInTheDocument();
  });

  it("scales the largest bar to full width against the max value", () => {
    render(
      <HorizontalBarList
        rows={[
          { key: "a", label: "Alpha", value: 10, color: "red" },
          { key: "b", label: "Beta", value: 5, color: "blue" },
        ]}
      />
    );

    const alphaRow = screen.getByText("Alpha").closest('[tabindex="0"]');
    const alphaBarFill = alphaRow?.querySelector('[data-testid="bar-fill"]');
    expect(alphaBarFill).toHaveStyle({ width: "100%" });

    const betaRow = screen.getByText("Beta").closest('[tabindex="0"]');
    const betaBarFill = betaRow?.querySelector('[data-testid="bar-fill"]');
    expect(betaBarFill).toHaveStyle({ width: "50%" });
  });

  it("renders a zero-width bar for a zero-value row without hiding the row", () => {
    render(<HorizontalBarList rows={[{ key: "a", label: "Alpha", value: 0, color: "red" }]} />);

    expect(screen.getByText("Alpha")).toBeInTheDocument();
    expect(screen.getByText("0")).toBeInTheDocument();
  });
});
