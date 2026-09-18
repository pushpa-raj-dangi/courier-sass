import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SidebarProvider } from "@/components/ui/sidebar";
import { AppSidebar } from "./app-sidebar";

vi.mock("next/navigation", () => ({
  usePathname: () => "/dashboard",
}));

function renderAppSidebar() {
  let resolveFetch!: (value: unknown) => void;
  global.fetch = vi.fn().mockReturnValue(
    new Promise((resolve) => {
      resolveFetch = resolve;
    })
  ) as unknown as typeof fetch;

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <SidebarProvider>
        <AppSidebar />
      </SidebarProvider>
    </QueryClientProvider>
  );

  return {
    resolveSession(organizationName: string) {
      resolveFetch({
        ok: true,
        status: 200,
        json: async () => ({
          userId: "u1",
          organizationId: "o1",
          organizationName,
          email: "a@b.com",
          fullName: "A B",
          permissions: ["report.read"],
          hasAllBranchesScope: true,
          branches: [],
        }),
      });
    },
  };
}

describe("AppSidebar", () => {
  it("shows a brand skeleton instead of the organization name while the session is loading", () => {
    renderAppSidebar();

    expect(screen.getByTestId("sidebar-brand-skeleton")).toBeInTheDocument();
    expect(screen.queryByText("Courier Platform")).not.toBeInTheDocument();
  });

  it("swaps the skeleton for the real organization name once the session loads", async () => {
    const { resolveSession } = renderAppSidebar();

    resolveSession("Sagarmatha Express");

    await waitFor(() => expect(screen.getByText("Sagarmatha Express")).toBeInTheDocument());
    expect(screen.queryByTestId("sidebar-brand-skeleton")).not.toBeInTheDocument();
  });
});
