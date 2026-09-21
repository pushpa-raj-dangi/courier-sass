import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { Package } from "lucide-react";
import { AuthShowcasePanel } from "./AuthShowcasePanel";

describe("AuthShowcasePanel", () => {
  it("shows a mono sample-AWB stamp above the headline", () => {
    render(
      <AuthShowcasePanel
        icon={Package}
        brand="Courier Platform"
        headline="Headline"
        description="Desc"
        stops={[{ label: "A", caption: "a", tone: "neutral" }]}
      />
    );
    expect(screen.getByText(/AWB NP2026-000000 · Sample/)).toBeInTheDocument();
  });
});
