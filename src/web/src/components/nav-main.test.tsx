import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SidebarProvider } from "@/components/ui/sidebar";
import { NavMain } from "./nav-main";

vi.mock("next/navigation", () => ({
  usePathname: () => "/dashboard",
}));

function renderNavMain(permissions: string[]) {
  global.fetch = vi.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => ({
      userId: "u1",
      organizationId: "o1",
      email: "a@b.com",
      fullName: "A B",
      permissions,
      hasAllBranchesScope: true,
      branches: [],
    }),
  }) as unknown as typeof fetch;

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <SidebarProvider>
        <NavMain />
      </SidebarProvider>
    </QueryClientProvider>
  );
}

describe("NavMain", () => {
  it("hides nav items the session lacks permission for", async () => {
    renderNavMain(["report.read"]);

    await waitFor(() => expect(screen.getByText("Dashboard")).toBeInTheDocument());
    expect(screen.queryByText("Shipments")).not.toBeInTheDocument();
  });

  it("shows nav items granted by the session's permissions", async () => {
    renderNavMain(["report.read", "shipment.read"]);

    await waitFor(() => expect(screen.getByText("Shipments")).toBeInTheDocument());
  });

  it("shows the Riders entry when the session has rider.read", async () => {
    renderNavMain(["report.read", "rider.read"]);

    await waitFor(() => expect(screen.getByText("Riders")).toBeInTheDocument());
  });

  it("shows My Deliveries when the session has delivery.attempt", async () => {
    renderNavMain(["report.read", "delivery.attempt"]);

    await waitFor(() => expect(screen.getByText("My Deliveries")).toBeInTheDocument());
  });

  it("shows the NDRs entry when the session has ndr.read", async () => {
    renderNavMain(["report.read", "ndr.read"]);

    await waitFor(() => expect(screen.getByText("NDRs")).toBeInTheDocument());
  });
});
