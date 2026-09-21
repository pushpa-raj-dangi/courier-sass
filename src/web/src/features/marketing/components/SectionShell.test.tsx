import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { SectionShell } from "./SectionShell";

describe("SectionShell", () => {
  it("renders an h2 wired to the section via aria-labelledby, with a numbered eyebrow and lede", () => {
    const { container } = render(
      <SectionShell id="demo" index={2} eyebrow="Scan" title="Demo title" lede="Demo lede">
        <p>child</p>
      </SectionShell>
    );
    const heading = screen.getByRole("heading", { level: 2, name: "Demo title" });
    expect(heading).toHaveAttribute("id", "demo-heading");
    expect(container.querySelector("section#demo")).toHaveAttribute("aria-labelledby", "demo-heading");
    expect(screen.getByText("02 — Scan")).toBeInTheDocument();
    expect(screen.getByText("Demo lede")).toBeInTheDocument();
    expect(screen.getByText("child")).toBeInTheDocument();
  });
});
