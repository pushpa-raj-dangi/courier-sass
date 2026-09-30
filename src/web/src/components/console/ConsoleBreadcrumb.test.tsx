import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { Package } from "lucide-react";
import { renderWithIntl } from "@/test/renderWithIntl";
import { ConsoleBreadcrumb } from "./ConsoleBreadcrumb";
import type { ConsoleNavGroup } from "./types";

let pathname = "/riders/live-map";
vi.mock("next/navigation", () => ({ usePathname: () => pathname }));

const nav: ConsoleNavGroup[] = [
  {
    id: "ops",
    label: "Operations",
    items: [
      { href: "/riders", label: "Riders", icon: Package, matchPrefix: true },
      { href: "/riders/live-map", label: "Live Map", icon: Package, matchPrefix: true },
    ],
  },
];

describe("ConsoleBreadcrumb", () => {
  it("shows group › most specific page, marking the page current", () => {
    pathname = "/riders/live-map";
    renderWithIntl(<ConsoleBreadcrumb nav={nav} />);

    expect(screen.getByRole("navigation", { name: "Breadcrumb" })).toBeInTheDocument();
    expect(screen.getByText("Operations")).toBeInTheDocument();
    expect(screen.getByText("Live Map")).toHaveAttribute("aria-current", "page");
  });

  it("renders nothing for a path outside the nav", () => {
    pathname = "/elsewhere";
    const { container } = renderWithIntl(<ConsoleBreadcrumb nav={nav} />);
    expect(container).toBeEmptyDOMElement();
  });
});
