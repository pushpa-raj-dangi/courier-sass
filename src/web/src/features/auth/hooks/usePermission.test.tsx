import { describe, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { usePermission } from "./usePermission";

function TestComponent({ permission }: { permission: string }) {
  const allowed = usePermission(permission);
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
      <TestComponent permission="shipment.create" />
    </QueryClientProvider>
  );
  return { ...result, restore: () => (global.fetch = originalFetch) };
}

describe("usePermission", () => {
  it("returns denied while there is no session", async () => {
    const { restore } = renderWithSession(null);
    await waitFor(() => screen.getByText("denied"));
    restore();
  });

  it("returns allowed when the permission is in the session's permission list", async () => {
    const { restore } = renderWithSession({
      userId: "u1",
      organizationId: "o1",
      email: "a@b.com",
      fullName: "A B",
      permissions: ["shipment.create"],
      hasAllBranchesScope: true,
      branches: [],
    });
    await waitFor(() => screen.getByText("allowed"));
    restore();
  });
});
