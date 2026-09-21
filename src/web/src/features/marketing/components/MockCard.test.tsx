import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MockCard, ScanBar } from "./MockCard";

describe("MockCard", () => {
  it("renders its label, children, and the Sample data disclosure", () => {
    render(<MockCard label="Scan received">body text</MockCard>);
    expect(screen.getByText("Scan received")).toBeInTheDocument();
    expect(screen.getByText("body text")).toBeInTheDocument();
    expect(screen.getByText("Sample data")).toBeInTheDocument();
  });
});

describe("ScanBar", () => {
  it("is decorative and carries the scan-line class", () => {
    const { container } = render(<ScanBar />);
    expect(container.firstElementChild).toHaveAttribute("aria-hidden", "true");
    expect(container.querySelector(".marketing-scan-line")).not.toBeNull();
  });
});
