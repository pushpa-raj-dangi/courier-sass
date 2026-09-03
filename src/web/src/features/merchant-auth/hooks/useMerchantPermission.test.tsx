import { describe, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useMerchantPermission } from "./useMerchantPermission";

function TestComponent({ permission }: { permission: string }) {
  const allowed = useMerchantPermission(permission);
  return <div>{allowed ? "allowed" : "denied"}</div>;
}

function renderWithSession(session: unknown) {
  const originalFetch = global.fetch;
  global.fetch = vi.fn().mockResolvedValue({
    ok: session !== null,
    status: session === null ? 401 : 200,
    json: async () => session,
  }) as unknown as typeof fetch;

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const result = render(
    <QueryClientProvider client={queryClient}>
      <TestComponent permission="merchant.staff.manage" />
    </QueryClientProvider>
  );
  return { ...result, restore: () => (global.fetch = originalFetch) };
}

describe("useMerchantPermission", () => {
  it("returns denied while there is no session", async () => {
    const { restore } = renderWithSession(null);
    await waitFor(() => screen.getByText("denied"));
    restore();
  });

  it("returns allowed when the permission is in the session's permission list", async () => {
    const { restore } = renderWithSession({
      merchantUserId: "mu1",
      merchantId: "m1",
      organizationId: "o1",
      merchantDisplayName: "Acme Traders",
      email: "owner@acme.test",
      fullName: "Owner Person",
      role: "Owner",
      permissions: ["merchant.staff.manage"],
      kycStatus: "Pending",
    });
    await waitFor(() => screen.getByText("allowed"));
    restore();
  });

  it("returns denied when the permission is absent for a Staff role", async () => {
    const { restore } = renderWithSession({
      merchantUserId: "mu2",
      merchantId: "m1",
      organizationId: "o1",
      merchantDisplayName: "Acme Traders",
      email: "staff@acme.test",
      fullName: "Staff Person",
      role: "Staff",
      permissions: ["shipment.read"],
      kycStatus: "Pending",
    });
    await waitFor(() => screen.getByText("denied"));
    restore();
  });
});
