import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { StatusBadge } from "./StatusBadge";

describe("StatusBadge", () => {
  it("renders the mapped label for a known status", () => {
    render(<StatusBadge status="IN_TRANSIT" />);
    expect(screen.getByText("In Transit")).toBeInTheDocument();
  });

  it("falls back to the raw status string for an unknown status", () => {
    render(<StatusBadge status="SOME_FUTURE_STATUS" />);
    expect(screen.getByText("SOME_FUTURE_STATUS")).toBeInTheDocument();
  });
});
