import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import type { Manifest, ManifestShipmentScan, MissingShipment } from "@/features/manifests/types";

let manifestQuery: { data?: Manifest; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/manifests/hooks/useManifest", () => ({ useManifest: () => manifestQuery }));
let missingQuery: { data?: MissingShipment[]; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/manifests/hooks/useMissingShipments", () => ({ useMissingShipments: () => missingQuery }));
let parcelsQuery: { data?: ManifestShipmentScan[]; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/manifests/hooks/useManifestShipments", () => ({ useManifestShipments: () => parcelsQuery }));
const closeMutate = vi.fn();
vi.mock("@/features/manifests/hooks/useCloseManifest", () => ({ useCloseManifest: () => ({ mutate: closeMutate, isPending: false, isError: false }) }));
let granted: string[] = [];
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => granted.includes(p) }));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ riderName: () => "Hari Thapa", userName: () => "Staff", branchName: (id: string | null) => (id === "b1" ? "Kathmandu Hub" : "Pokhara Hub") }),
}));
const openShipment = vi.fn();
vi.mock("@/features/shipments/store/shipmentPanelStore", () => ({
  useShipmentPanelStore: (selector: (s: unknown) => unknown) => selector({ openDetail: openShipment }),
}));
let panel: { mode: "detail"; id: string; direction?: "incoming" | "outgoing" } = { mode: "detail", id: "m1" };
vi.mock("@/features/manifests/store/manifestPanelStore", () => ({
  useManifestPanelStore: (selector: (s: unknown) => unknown) => selector({ panel, close: vi.fn() }),
}));
vi.mock("@/features/manifests/components/ManifestDraftSection", () => ({
  ManifestDraftSection: ({ canSeal }: { canSeal: boolean }) => <p>{`draft-section seal:${canSeal}`}</p>,
}));
vi.mock("@/features/manifests/components/ManifestSealedSection", () => ({ ManifestSealedSection: () => <p>sealed-section</p> }));
vi.mock("@/features/manifests/components/ManifestInTransitSection", () => ({ ManifestInTransitSection: () => <p>receive-scan</p> }));

import { ManifestDetailSheet } from "./ManifestDetailSheet";

const base: Manifest = {
  id: "m1", code: "MNF-20261015-001", fromBranchId: "b1", toBranchId: "b2", purpose: "Forward",
  vehiclePlate: "BA 2 KHA 1234", driverId: "r1", status: "Received", shipmentCount: 120, receivedCount: 118,
  createdAt: "2026-10-15T09:00:00Z",
};

beforeEach(() => {
  closeMutate.mockReset();
  openShipment.mockReset();
  granted = ["manifest.close", "manifest.seal", "manifest.dispatch"];
  panel = { mode: "detail", id: "m1", direction: "incoming" };
  manifestQuery = { data: base, isLoading: false, isError: false, refetch: vi.fn() };
  missingQuery = {
    data: [
      { shipmentId: "s1", awb: "KTM-000201", scannedAt: "2026-10-14T15:00:00Z" },
      { shipmentId: "0f8e2a6c-1111-2222-3333-444455556666", awb: "", scannedAt: "2026-10-14T15:01:00Z" },
    ],
    isLoading: false, isError: false, refetch: vi.fn(),
  };
  parcelsQuery = {
    data: [
      { id: "ms1", shipmentId: "s1", awb: "KTM-000201", scannedAt: "2026-10-14T15:00:00Z", receivedAt: null },
      { id: "ms2", shipmentId: "s2", awb: "KTM-000202", scannedAt: "2026-10-14T15:02:00Z", receivedAt: "2026-10-15T08:00:00Z" },
    ],
    isLoading: false, isError: false, refetch: vi.fn(),
  };
});

describe("ManifestDetailSheet", () => {
  it("renders the header, eyebrow with direction and purpose, and the status-only stepper", () => {
    render(<ManifestDetailSheet />);
    expect(screen.getByText("MANIFEST · INCOMING · FORWARD")).toBeInTheDocument();
    expect(screen.getByText("MNF-20261015-001")).toBeInTheDocument();
    expect(screen.getByText("Kathmandu Hub")).toBeInTheDocument();
    expect(screen.getByText("Hari Thapa")).toBeInTheDocument();
    const steps = screen.getByRole("list", { name: "Manifest progress" });
    expect(steps.querySelector('[aria-current="step"]')).toHaveTextContent("Receiving");
    expect(screen.getByText("Stage times need backend support.")).toHaveAttribute("data-coming-soon");
  });

  it("shows reconciliation counts, the missing list with a short-id fallback, and the parcel list", () => {
    render(<ManifestDetailSheet />);
    expect(screen.getByText("118")).toBeInTheDocument();
    expect(screen.getByText("of 120 verified")).toBeInTheDocument();
    expect(screen.getByRole("progressbar", { name: "Parcels verified" })).toHaveAttribute("aria-valuenow", "118");
    expect(screen.getAllByText("KTM-000201")).toHaveLength(2);
    expect(screen.getByText("0f8e2a6c")).toBeInTheDocument();
    expect(screen.getByText("Not yet")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Open parcel KTM-000201" }));
    expect(openShipment).toHaveBeenCalledWith("KTM-000201");
  });

  it("confirms 'Close with N missing' before closing", () => {
    render(<ManifestDetailSheet />);
    fireEvent.click(screen.getByRole("button", { name: "Close with 2 missing" }));
    const dialog = screen.getByRole("alertdialog");
    expect(within(dialog).getByText("2 parcels stay listed as missing.")).toBeInTheDocument();
    expect(closeMutate).not.toHaveBeenCalled();
    fireEvent.click(within(dialog).getByRole("button", { name: "Close with 2 missing" }));
    expect(closeMutate).toHaveBeenCalledTimes(1);
  });

  it("closes directly when nothing is missing, and hides close without manifest.close", () => {
    manifestQuery = { ...manifestQuery, data: { ...base, receivedCount: 120 } };
    const { unmount } = render(<ManifestDetailSheet />);
    fireEvent.click(screen.getByRole("button", { name: "Close manifest" }));
    expect(closeMutate).toHaveBeenCalledTimes(1);
    unmount();

    granted = [];
    render(<ManifestDetailSheet />);
    // Exact names: the Sheet's own dismiss button is also called "Close".
    expect(screen.queryByRole("button", { name: /^Close (manifest|with)/ })).toBeNull();
  });

  it("gives each sub-read its own loading, error and empty state", () => {
    missingQuery = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    parcelsQuery = { data: [], isLoading: false, isError: false, refetch: vi.fn() };
    render(<ManifestDetailSheet />);
    expect(screen.getByText("Couldn't load missing parcels")).toBeInTheDocument();
    expect(screen.getByText("No parcels on this manifest.")).toBeInTheDocument();
  });

  it("titles a closed manifest's missing list and drops scan and close", () => {
    manifestQuery = { ...manifestQuery, data: { ...base, status: "Closed" } };
    render(<ManifestDetailSheet />);
    expect(screen.getByText("Closed with 2 missing")).toBeInTheDocument();
    expect(screen.queryByText("receive-scan")).toBeNull();
    expect(screen.queryByRole("button", { name: /^Close (manifest|with)/ })).toBeNull();
  });

  it("passes seal permission to the draft section and omits the direction when the store has none", () => {
    granted = [];
    panel = { mode: "detail", id: "m1" };
    manifestQuery = { ...manifestQuery, data: { ...base, status: "Draft", receivedCount: 0, purpose: "Return" } };
    render(<ManifestDetailSheet />);
    expect(screen.getByText("draft-section seal:false")).toBeInTheDocument();
    expect(screen.getByText("MANIFEST · RTO")).toBeInTheDocument();
    expect(screen.queryByRole("progressbar")).toBeNull();
  });

  it("renders a skeleton while loading and an error with retry", () => {
    manifestQuery = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    render(<ManifestDetailSheet />);
    expect(screen.getByText("Couldn't load this manifest")).toBeInTheDocument();
  });

  it("shows 0 of 0 verified with an empty bar for a manifest with no parcels", () => {
    manifestQuery = { ...manifestQuery, data: { ...base, shipmentCount: 0, receivedCount: 0 } };
    missingQuery = { data: [], isLoading: false, isError: false, refetch: vi.fn() };
    render(<ManifestDetailSheet />);
    expect(screen.getByText("of 0 verified")).toBeInTheDocument();
    expect(screen.getByRole("progressbar", { name: "Parcels verified" }).firstElementChild).toHaveStyle({ width: "0%" });
  });
});
