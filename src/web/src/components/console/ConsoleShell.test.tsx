import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { Package } from "lucide-react";
import { renderWithIntl } from "@/test/renderWithIntl";
import { ConsoleShell } from "./ConsoleShell";

vi.mock("next/navigation", () => ({ usePathname: () => "/dashboard" }));

describe("ConsoleShell", () => {
  it("renders brand, nav, header slots, banners and children", () => {
    renderWithIntl(
      <ConsoleShell
        brand={{ icon: Package, name: "Acme Courier" }}
        nav={[{ id: "ops", label: "Operations", items: [{ href: "/dashboard", label: "Dashboard", icon: Package }] }]}
        headerStart={<div>Search slot</div>}
        headerEnd={<div>User slot</div>}
        banners={<div>Trial banner</div>}
      >
        <p>Page body</p>
      </ConsoleShell>
    );

    expect(screen.getByText("Acme Courier")).toBeInTheDocument();
    // BreadcrumbPage also renders role="link" with aria-disabled, so check for the nav link specifically
    const dashboardLinks = screen.getAllByRole("link", { name: "Dashboard" });
    const navLink = dashboardLinks.find((el) => !el.hasAttribute("aria-disabled"));
    expect(navLink).toBeInTheDocument();
    expect(screen.getByText("Search slot")).toBeInTheDocument();
    expect(screen.getByText("User slot")).toBeInTheDocument();
    expect(screen.getByText("Trial banner")).toBeInTheDocument();
    expect(screen.getByText("Page body")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Notifications" })).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Breadcrumb" })).toBeInTheDocument();
  });
});
