import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { IntlTestProvider } from "@/test/renderWithIntl";
import { useStaffNavGroups } from "./useStaffNavGroups";

function mockSession(permissions: string[]) {
  global.fetch = vi.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => ({
      userId: "u1",
      organizationId: "o1",
      organizationName: "Acme",
      email: "a@b.com",
      fullName: "A B",
      permissions,
      hasAllBranchesScope: true,
      branches: [],
    }),
  }) as unknown as typeof fetch;
}

function wrapper(locale: "en" | "ne" = "en") {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <IntlTestProvider locale={locale}>{children}</IntlTestProvider>
      </QueryClientProvider>
    );
  };
}

const labels = (groups: { label: string; items: { label: string }[] }[]) =>
  groups.map((g) => [g.label, g.items.map((i) => i.label)]);

describe("useStaffNavGroups", () => {
  it("groups permitted items under their headings in order", async () => {
    mockSession(["report.read", "shipment.read", "cod.read", "branch.read", "audit.read"]);
    const { result } = renderHook(() => useStaffNavGroups(), { wrapper: wrapper() });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(labels(result.current.groups)).toEqual([
      ["Operations", ["Dashboard", "Shipments"]],
      ["Finance", ["COD"]],
      ["Network", ["Branches"]],
      ["Admin", ["Audit Logs"]],
    ]);
  });

  it("omits groups whose items are all hidden", async () => {
    mockSession(["report.read"]);
    const { result } = renderHook(() => useStaffNavGroups(), { wrapper: wrapper() });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.groups.map((g) => g.label)).toEqual(["Operations"]);
  });

  it("returns no groups for a session with no permissions", async () => {
    mockSession([]);
    const { result } = renderHook(() => useStaffNavGroups(), { wrapper: wrapper() });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.groups).toEqual([]);
  });

  it("shows Admin when the session has either role.manage or user.read", async () => {
    mockSession(["user.read"]);
    const { result } = renderHook(() => useStaffNavGroups(), { wrapper: wrapper() });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(labels(result.current.groups)).toEqual([["Admin", ["Admin"]]]);
  });

  it("uses prefix matching so nested routes resolve", async () => {
    mockSession(["rider.read", "rider.location.read"]);
    const { result } = renderHook(() => useStaffNavGroups(), { wrapper: wrapper() });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.groups[0].items.every((i) => i.matchPrefix)).toBe(true);
  });

  it("translates headings and labels in Nepali", async () => {
    mockSession(["report.read", "shipment.read"]);
    const { result } = renderHook(() => useStaffNavGroups(), { wrapper: wrapper("ne") });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(labels(result.current.groups)).toEqual([["सञ्चालन", ["ड्यासबोर्ड", "ढुवानीहरू"]]]);
  });
});
