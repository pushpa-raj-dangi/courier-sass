import { describe, expect, it, vi } from "vitest";
import { renderHook } from "@testing-library/react";

let canReadUsers = false;
const canReadRiders = true;
vi.mock("@/features/auth/hooks/usePermission", () => ({
  usePermission: (p: string) => (p === "user.read" ? canReadUsers : p === "rider.read" ? canReadRiders : false),
}));
const useAdminUsers = vi.fn();
vi.mock("@/features/admin/hooks/useAdminUsers", () => ({ useAdminUsers: (...a: unknown[]) => useAdminUsers(...a) }));
const useRiders = vi.fn();
vi.mock("@/features/riders/hooks/useRiders", () => ({ useRiders: (...a: unknown[]) => useRiders(...a) }));
vi.mock("@/features/branches/hooks/useBranches", () => ({ useBranches: () => ({ data: [{ id: "b1", name: "Lalitpur Branch", code: "LAL" }] }) }));

import { usePeopleNames } from "./usePeopleNames";

describe("usePeopleNames", () => {
  it("falls back to generic labels without user.read", () => {
    canReadUsers = false;
    useAdminUsers.mockReturnValue({ data: undefined });
    useRiders.mockReturnValue({ data: [{ id: "r1", userId: "u9", licenseNumber: "L-1", isActive: true }] });
    const { result } = renderHook(() => usePeopleNames());
    expect(result.current.userName("u1")).toBe("Staff");
    expect(result.current.riderName("r1")).toBe("Rider");
    expect(result.current.branchName("b1")).toBe("Lalitpur Branch");
    expect(result.current.branchName(null)).toBe("—");
    expect(useAdminUsers).toHaveBeenCalledWith({ pageSize: 200, enabled: false });
  });

  it("resolves staff and rider names with user.read", () => {
    canReadUsers = true;
    useAdminUsers.mockReturnValue({ data: { items: [{ id: "u9", fullName: "Bikash Tamang" }, { id: "u1", fullName: "Pooja M" }] } });
    useRiders.mockReturnValue({ data: [{ id: "r1", userId: "u9", licenseNumber: "L-1", isActive: true }] });
    const { result } = renderHook(() => usePeopleNames());
    expect(result.current.userName("u1")).toBe("Pooja M");
    expect(result.current.riderName("r1")).toBe("Bikash Tamang");
    expect(result.current.userName("unknown")).toBe("Staff");
  });
});
