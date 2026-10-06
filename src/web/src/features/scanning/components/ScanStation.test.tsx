import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderWithIntl } from "@/test/renderWithIntl";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/scan",
  useSearchParams: () => new URLSearchParams(search),
}));
let permissions: string[] = [];
vi.mock("@/features/auth/hooks/useSession", () => ({
  useSession: () => ({ data: { permissions, hasAllBranchesScope: true, branches: [] } }),
}));
vi.mock("@/components/scanner/BarcodeScanner", () => ({ BarcodeScanner: () => <div>camera</div> }));
const pickedManifest = { id: "m1", code: "MNF-1", fromBranchId: "b1", toBranchId: "b2", purpose: "Forward", vehiclePlate: null, driverId: null, status: "Draft", shipmentCount: 0, createdAt: "" };
vi.mock("@/features/scanning/components/ScanContextPicker", () => ({
  ScanContextPicker: ({ onChange }: { onChange: (c: unknown) => void }) => (
    <button onClick={() => onChange({ kind: "manifest", manifest: pickedManifest })}>pick-manifest</button>
  ),
}));
vi.mock("@/features/scanning/components/ReconciliationPanel", () => ({ ReconciliationPanel: () => <div>reconciliation</div> }));
vi.mock("@/features/scanning/beep", () => ({ playScanTone: vi.fn() }));
// Mocked so lookup queries (branches/users/riders) never consume the clientFetch responses
// queued for the scan calls below.
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ branchName: () => "Branch", riderName: () => "Rider", userName: () => "Staff" }),
}));
const clientFetch = vi.fn();
vi.mock("@/lib/api/clientFetch", async (orig) => ({ ...(await orig<object>()), clientFetch: (...a: unknown[]) => clientFetch(...a) }));

import { ScanStation } from "./ScanStation";

function renderStation() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithIntl(
    <QueryClientProvider client={qc}>
      <ScanStation />
    </QueryClientProvider>
  );
}

describe("ScanStation", () => {
  beforeEach(() => {
    search = "";
    replace.mockReset();
    clientFetch.mockReset();
    permissions = ["shipment.scan"];
  });

  it("shows only modes the user may use", () => {
    renderStation();
    expect(screen.getByRole("tab", { name: "Receive" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Sort" })).toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: "Dispatch" })).toBeNull();
    expect(screen.queryByRole("tab", { name: "Rider handover" })).toBeNull();
  });

  it("receives at origin from the manual input without a manifest", async () => {
    clientFetch.mockResolvedValueOnce({ id: "s1", awb: "QA-1", recipientName: "Sita", codAmount: 0 }).mockResolvedValueOnce({});
    renderStation();
    const input = screen.getByRole("textbox", { name: /scan or type awb/i });
    fireEvent.change(input, { target: { value: "qa-1" } });
    fireEvent.keyDown(input, { key: "Enter" });
    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("Accepted"));
    expect(clientFetch.mock.calls[1][0]).toBe("/api/shipments/s1/receive-origin");
    expect(screen.getByText("1 scans this session")).toBeInTheDocument();
  });

  it("blocks scanning until required context is chosen", () => {
    permissions = ["manifest.create", "manifest.read"];
    search = "mode=dispatch";
    renderStation();
    expect(screen.getByRole("textbox", { name: /scan or type awb/i })).toBeDisabled();
    expect(screen.getByText("Choose a manifest to start scanning.")).toBeInTheDocument();
  });

  it("sort mode is a needs-backend slot", () => {
    search = "mode=sort";
    renderStation();
    expect(screen.getByRole("region", { name: "Sorting" })).toHaveAttribute("data-coming-soon");
    expect(screen.queryByRole("textbox", { name: /scan or type awb/i })).toBeNull();
  });

  it("switching mode writes it to the URL", () => {
    permissions = ["shipment.scan", "manifest.create", "manifest.read"];
    renderStation();
    fireEvent.mouseDown(screen.getByRole("tab", { name: "Dispatch" }));
    expect(replace).toHaveBeenCalledWith("/scan?mode=dispatch", { scroll: false });
  });

  it("shows a no-access message with no scan permissions", () => {
    permissions = [];
    renderStation();
    expect(screen.getByText("You don't have permission to scan parcels.")).toBeInTheDocument();
  });
});

describe("ScanStation live manifest counter", () => {
  it("reflects the refetched manifest after a successful dispatch scan", async () => {
    permissions = ["manifest.create", "manifest.read"];
    search = "mode=dispatch";
    let count = 0;
    clientFetch.mockImplementation(async (url: string) => {
      if (url === "/api/manifests/m1") return { ...pickedManifest, shipmentCount: count };
      if (url === "/api/manifests/m1/scan") {
        count = 1;
        return {};
      }
      throw new Error(`unexpected ${url}`);
    });
    renderStation();
    fireEvent.click(screen.getByText("pick-manifest"));
    const card = await screen.findByRole("region", { name: "Manifest" });
    expect(card).toHaveTextContent("0 scanned");
    const input = screen.getByRole("textbox", { name: /scan or type awb/i });
    fireEvent.change(input, { target: { value: "qa-1" } });
    fireEvent.keyDown(input, { key: "Enter" });
    await waitFor(() => expect(screen.getByRole("region", { name: "Manifest" })).toHaveTextContent("1 scanned"));
  });
});
