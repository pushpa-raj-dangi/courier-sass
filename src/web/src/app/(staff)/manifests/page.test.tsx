import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import type { Manifest } from "@/features/manifests/types";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/manifests",
  useSearchParams: () => new URLSearchParams(search),
}));

const useManifests = vi.fn();
vi.mock("@/features/manifests/hooks/useManifests", () => ({
  useManifests: (...args: unknown[]) => useManifests(...args),
}));
let summary: { data?: Record<string, number>; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/manifests/hooks/useManifestsSummary", () => ({ useManifestsSummary: () => summary }));
vi.mock("@/features/auth/hooks/useSession", () => ({
  useSession: () => ({ data: { userId: "u1", hasAllBranchesScope: true, branches: [] } }),
}));
vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b1", code: "KTM", name: "Kathmandu Hub" }, { id: "b2", code: "PKR", name: "Pokhara Hub" }] }),
}));
let granted: string[] = [];
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => granted.includes(p) }));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ riderName: (id: string | null) => (id ? "Hari Thapa" : "—"), userName: () => "Staff", branchName: () => "Branch" }),
}));
const openDetail = vi.fn();
const openCreate = vi.fn();
vi.mock("@/features/manifests/store/manifestPanelStore", () => ({
  useManifestPanelStore: (selector: (s: unknown) => unknown) => selector({ panel: { mode: "closed" }, openCreate, openDetail }),
}));

import ManifestsPage from "./page";

function manifest(overrides: Partial<Manifest>): Manifest {
  return {
    id: "m1", code: "MNF-20261015-001", fromBranchId: "b1", toBranchId: "b2", purpose: "Forward",
    vehiclePlate: "BA 2 KHA 1234", driverId: "r1", status: "Received", shipmentCount: 120, receivedCount: 118,
    createdAt: "2026-10-15T09:00:00Z", ...overrides,
  };
}

beforeEach(() => {
  replace.mockReset();
  openDetail.mockReset();
  search = "";
  granted = ["manifest.create", "manifest.receive"];
  summary = { data: { Draft: 1, Sealed: 2, InTransit: 5, Received: 3, Closed: 40 }, isLoading: false, isError: false, refetch: vi.fn() };
  useManifests.mockReset();
  useManifests.mockReturnValue({ data: [manifest({})], isLoading: false, isError: false, refetch: vi.fn() });
});

describe("ManifestsPage", () => {
  it("shows the five-stage strip from the summary and highlights the busiest open stage", () => {
    render(<ManifestsPage />);
    const strip = screen.getByRole("list", { name: "Manifest lifecycle" });
    expect(strip).toHaveTextContent("Receiving");
    expect(screen.getByRole("button", { name: /In transit/ })).toHaveAttribute("aria-current", "step");
    expect(screen.getByText("Across your branches")).toBeInTheDocument();
  });

  it("never renders zeros while the summary loads or fails", () => {
    summary = { data: undefined, isLoading: true, isError: false, refetch: vi.fn() };
    const { unmount } = render(<ManifestsPage />);
    expect(screen.getByRole("status", { name: "Loading manifest lifecycle" })).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Manifest lifecycle" })).toBeNull();
    unmount();

    summary = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    render(<ManifestsPage />);
    expect(screen.getByText("Couldn't load manifest counts")).toBeInTheDocument();
  });

  it("defaults to the incoming tab and writes tab and stage changes to the URL", () => {
    render(<ManifestsPage />);
    expect(useManifests).toHaveBeenCalledWith(undefined, undefined, { direction: "incoming" });
    fireEvent.mouseDown(screen.getByRole("tab", { name: "Outgoing" }));
    expect(replace).toHaveBeenCalledWith("/manifests?tab=outgoing", { scroll: false });
    fireEvent.click(screen.getByRole("button", { name: /Sealed/ }));
    expect(replace).toHaveBeenCalledWith("/manifests?status=Sealed", { scroll: false });
  });

  it("reads tab, status and branch from the URL; the All tab sends no direction", () => {
    search = "tab=all&status=Received&branch=b2";
    render(<ManifestsPage />);
    expect(useManifests).toHaveBeenCalledWith("b2", "Received", { direction: undefined });
    fireEvent.click(screen.getByRole("button", { name: "Clear status filter Receiving" }));
    expect(replace).toHaveBeenCalledWith("/manifests?tab=all&branch=b2", { scroll: false });
  });

  it("words the parcels column by stage and routes with branch codes", () => {
    useManifests.mockReturnValue({
      data: [manifest({}), manifest({ id: "m2", code: "MNF-20261015-002", status: "Draft", shipmentCount: 5, receivedCount: 0, vehiclePlate: null, driverId: null })],
      isLoading: false, isError: false, refetch: vi.fn(),
    });
    render(<ManifestsPage />);
    expect(screen.getByText("118 / 120")).toBeInTheDocument();
    expect(screen.getByText("2 missing")).toBeInTheDocument();
    expect(screen.getByText("5 parcels")).toBeInTheDocument();
    expect(screen.getAllByText("KTM → PKR")).toHaveLength(2);
    expect(screen.getByText("Not assigned")).toBeInTheDocument();
    expect(screen.getByText("Hari Thapa")).toBeInTheDocument();
    expect(screen.getByText(/All my branches · 2 open/)).toBeInTheDocument();
  });

  it("opens the Sheet with the direction only when a branch is picked", () => {
    render(<ManifestsPage />);
    fireEvent.click(screen.getByRole("button", { name: /MNF-20261015-001/ }));
    expect(openDetail).toHaveBeenCalledWith("m1", undefined);

    search = "branch=b2";
    render(<ManifestsPage />);
    fireEvent.click(screen.getAllByRole("button", { name: /MNF-20261015-001/ })[1]);
    expect(openDetail).toHaveBeenLastCalledWith("m1", "incoming");
  });

  it("hints at picking a branch only for all-branches scope with no branch picked", () => {
    const { unmount } = render(<ManifestsPage />);
    expect(screen.getByText("Pick a branch to split incoming and outgoing.")).toBeInTheDocument();
    unmount();

    search = "branch=b2";
    render(<ManifestsPage />);
    expect(screen.queryByText("Pick a branch to split incoming and outgoing.")).toBeNull();
  });

  it("shows an error state with retry when the list fails, and gates the header actions", () => {
    granted = [];
    useManifests.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn() });
    render(<ManifestsPage />);
    expect(screen.getByText("Couldn't load manifests")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /New manifest/ })).toBeNull();
    expect(screen.queryByRole("link", { name: /Receive at scan station/ })).toBeNull();
  });
});
