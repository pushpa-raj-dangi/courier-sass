import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SidebarProvider } from "@/components/ui/sidebar";
import { MerchantNavMain } from "./MerchantNavMain";

vi.mock("next/navigation", () => ({
  usePathname: () => "/merchant/dashboard",
}));

function renderNavMain(permissions: string[]) {
  global.fetch = vi.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => ({
      merchantUserId: "mu1",
      merchantId: "m1",
      organizationId: "o1",
      merchantDisplayName: "Acme Traders",
      email: "owner@acme.test",
      fullName: "Owner Person",
      role: "Owner",
      permissions,
      kycStatus: "Pending",
    }),
  }) as unknown as typeof fetch;

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <SidebarProvider>
        <MerchantNavMain />
      </SidebarProvider>
    </QueryClientProvider>
  );
}

describe("MerchantNavMain", () => {
  it("always shows Dashboard regardless of permissions", async () => {
    renderNavMain([]);

    await waitFor(() => expect(screen.getByText("Dashboard")).toBeInTheDocument());
    expect(screen.queryByText("Staff")).not.toBeInTheDocument();
  });

  it("hides Staff and API Keys for a Staff-role session", async () => {
    renderNavMain(["shipment.read", "merchant.pickups.read", "merchant.cod.read", "merchant.kyc.upload"]);

    await waitFor(() => expect(screen.getByText("Shipments")).toBeInTheDocument());
    expect(screen.queryByText("Staff")).not.toBeInTheDocument();
    expect(screen.queryByText("API Keys")).not.toBeInTheDocument();
  });

  it("shows Staff and API Keys for an Owner-role session", async () => {
    renderNavMain(["merchant.staff.manage", "merchant.apikeys.manage"]);

    await waitFor(() => expect(screen.getByText("Staff")).toBeInTheDocument());
    expect(screen.getByText("API Keys")).toBeInTheDocument();
  });
});
