import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { Package, Wallet } from "lucide-react";
import { renderWithIntl } from "@/test/renderWithIntl";
import { SidebarProvider } from "@/components/ui/sidebar";
import { ConsoleSidebar } from "./ConsoleSidebar";
import type { ConsoleNavGroup } from "./types";

vi.mock("next/navigation", () => ({ usePathname: () => "/shipments" }));

const nav: ConsoleNavGroup[] = [
  {
    id: "ops",
    label: "Operations",
    items: [
      { href: "/dashboard", label: "Dashboard", icon: Package },
      { href: "/shipments", label: "Shipments", icon: Package, matchPrefix: true },
    ],
  },
  { id: "empty", label: "Empty Group", items: [] },
  { id: "finance", label: "Finance", items: [{ href: "/cod", label: "COD", icon: Wallet }] },
];

function renderSidebar(props: Partial<Parameters<typeof ConsoleSidebar>[0]> = {}, defaultOpen = true) {
  return renderWithIntl(
    <SidebarProvider defaultOpen={defaultOpen}>
      <ConsoleSidebar
        brand={{ icon: Package, name: "Sagarmatha Express", subtitle: "Operations console" }}
        nav={nav}
        {...props}
      />
    </SidebarProvider>
  );
}

describe("ConsoleSidebar", () => {
  it("renders brand name and subtitle", () => {
    renderSidebar();
    expect(screen.getByText("Sagarmatha Express")).toBeInTheDocument();
    expect(screen.getByText("Operations console")).toBeInTheDocument();
  });

  it("shows a brand skeleton while the brand is loading", () => {
    renderSidebar({ brand: { icon: Package, name: "X", isLoading: true } });
    expect(screen.getByTestId("sidebar-brand-skeleton")).toBeInTheDocument();
    expect(screen.queryByText("X")).not.toBeInTheDocument();
  });

  it("renders group headings and skips groups with no items", () => {
    renderSidebar();
    expect(screen.getByText("Operations")).toBeInTheDocument();
    expect(screen.getByText("Finance")).toBeInTheDocument();
    expect(screen.queryByText("Empty Group")).not.toBeInTheDocument();
  });

  it("marks only the active item with aria-current=page", () => {
    renderSidebar();
    expect(screen.getByRole("link", { name: "Shipments" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "Dashboard" })).not.toHaveAttribute("aria-current");
  });

  it("renders the scope slot when expanded", () => {
    renderSidebar({ scope: <div>Scope control</div> });
    expect(screen.getByText("Scope control")).toBeInTheDocument();
  });

  it("shows nav skeleton rows instead of items while navLoading", () => {
    renderSidebar({ navLoading: true });
    expect(screen.getAllByTestId("nav-item-skeleton").length).toBeGreaterThan(0);
    expect(screen.queryByRole("link", { name: "Dashboard" })).not.toBeInTheDocument();
  });

  it("keeps accessible link names when collapsed to the icon rail", () => {
    renderSidebar({}, false);
    expect(screen.getByRole("link", { name: "Dashboard" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "COD" })).toBeInTheDocument();
  });
});
