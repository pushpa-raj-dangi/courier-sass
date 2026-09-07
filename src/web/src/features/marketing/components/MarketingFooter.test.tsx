import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MarketingFooter } from "./MarketingFooter";
import { FOOTER_LINKS } from "../content";

describe("MarketingFooter", () => {
  it("renders every footer link with the correct href", () => {
    render(<MarketingFooter />);
    for (const link of FOOTER_LINKS) {
      expect(screen.getByRole("link", { name: link.label })).toHaveAttribute("href", link.href);
    }
  });

  it("renders the Courier Platform wordmark", () => {
    render(<MarketingFooter />);
    expect(screen.getByText("Courier Platform")).toBeInTheDocument();
  });
});
