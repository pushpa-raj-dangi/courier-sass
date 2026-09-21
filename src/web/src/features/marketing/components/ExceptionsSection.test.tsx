import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { ExceptionsSection } from "./ExceptionsSection";
import { DELIVERY_FAILURE_REASONS, NDR_STAGES, RTO_STAGES } from "../content";

describe("ExceptionsSection", () => {
  it("shows failure reasons plus the NDR and RTO workflows", () => {
    const { container } = render(<ExceptionsSection />);
    expect(container.querySelector("section#exceptions")).not.toBeNull();
    for (const reason of DELIVERY_FAILURE_REASONS) expect(screen.getByText(reason)).toBeInTheDocument();
    expect(screen.getByRole("list", { name: /ndr workflow/i }).querySelectorAll("li")).toHaveLength(NDR_STAGES.length);
    expect(screen.getByRole("list", { name: /rto workflow/i }).querySelectorAll("li")).toHaveLength(RTO_STAGES.length);
  });
});
