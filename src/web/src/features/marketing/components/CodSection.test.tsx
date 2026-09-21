import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { CodSection } from "./CodSection";
import { COD_STAGES } from "../content";

describe("CodSection", () => {
  it("is the #cod anchor with the waterfall figure and all 7 lifecycle stages", () => {
    const { container } = render(<CodSection />);
    expect(container.querySelector("section#cod")).not.toBeNull();
    expect(screen.getByRole("figure", { name: /cod/i })).toBeInTheDocument();
    expect(screen.getByRole("list", { name: /cod lifecycle/i }).querySelectorAll("li")).toHaveLength(COD_STAGES.length);
  });
});
