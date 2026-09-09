import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

vi.mock("next/navigation", () => ({
  usePathname: () => "/platform/plans",
}));

import { PlatformNav } from "./PlatformNav";

describe("PlatformNav", () => {
  it("renders all four platform nav links", () => {
    render(<PlatformNav />);

    expect(screen.getByText("Dashboard")).toBeInTheDocument();
    expect(screen.getByText("Organizations")).toBeInTheDocument();
    expect(screen.getByText("Plans")).toBeInTheDocument();
    expect(screen.getByText("Invoices")).toBeInTheDocument();
  });

  it("highlights the active page based on the current pathname", () => {
    render(<PlatformNav />);

    expect(screen.getByText("Plans")).toHaveClass("text-foreground");
    expect(screen.getByText("Dashboard")).not.toHaveClass("text-foreground");
  });
});
