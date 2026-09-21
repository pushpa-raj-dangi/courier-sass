import { describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import { renderWithIntl } from "@/test/renderWithIntl";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SidebarProvider } from "@/components/ui/sidebar";
import { NavMain } from "./nav-main";

vi.mock("next/navigation", () => ({
  usePathname: () => "/dashboard",
}));

function renderNavMain(permissions: string[], locale?: "en" | "ne") {
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
  return renderWithIntl(
    <QueryClientProvider client={queryClient}>
      <SidebarProvider>
        <NavMain />
      </SidebarProvider>
    </QueryClientProvider>,
    { locale }
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

  it("shows the Return Shipments entry when the session has rto.read", async () => {
    renderNavMain(["report.read", "rto.read"]);

    await waitFor(() => expect(screen.getByText("Return Shipments")).toBeInTheDocument());
  });

  it("shows the Audit Logs entry when the session has audit.read", async () => {
    renderNavMain(["report.read", "audit.read"]);

    await waitFor(() => expect(screen.getByText("Audit Logs")).toBeInTheDocument());
  });

  it("renders nav labels in Nepali", async () => {
    renderNavMain(["report.read", "shipment.read"], "ne");

    await waitFor(() => expect(screen.getByText("ड्यासबोर्ड")).toBeInTheDocument());
    expect(screen.getByText("ढुवानीहरू")).toBeInTheDocument();
    expect(screen.getByText("प्लेटफर्म")).toBeInTheDocument();
    expect(screen.queryByText("Dashboard")).not.toBeInTheDocument();
  });

  it("hides the Audit Logs entry when the session lacks audit.read", async () => {
    renderNavMain(["report.read"]);

    await waitFor(() => expect(screen.getByText("Dashboard")).toBeInTheDocument());
    expect(screen.queryByText("Audit Logs")).not.toBeInTheDocument();
  });

  it("shows skeleton rows instead of nav items while the session is loading, then swaps in the real items", async () => {
    let resolveFetch!: (value: unknown) => void;
    global.fetch = vi.fn().mockReturnValue(
      new Promise((resolve) => {
        resolveFetch = resolve;
      })
    ) as unknown as typeof fetch;

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    renderWithIntl(
      <QueryClientProvider client={queryClient}>
        <SidebarProvider>
          <NavMain />
        </SidebarProvider>
      </QueryClientProvider>
    );

    expect(screen.getAllByTestId("nav-item-skeleton").length).toBeGreaterThan(0);
    expect(screen.queryByText("Dashboard")).not.toBeInTheDocument();

    resolveFetch({
      ok: true,
      status: 200,
      json: async () => ({
        userId: "u1",
        organizationId: "o1",
        email: "a@b.com",
        fullName: "A B",
        permissions: ["report.read"],
        hasAllBranchesScope: true,
        branches: [],
      }),
    });

    await waitFor(() => expect(screen.getByText("Dashboard")).toBeInTheDocument());
    expect(screen.queryByTestId("nav-item-skeleton")).not.toBeInTheDocument();
  });
});
