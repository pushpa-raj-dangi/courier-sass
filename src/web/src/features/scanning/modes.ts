import { clientFetch } from "@/lib/api/clientFetch";
import type { Manifest } from "@/features/manifests/types";
import type { Rider } from "@/features/riders/types";
import type { Shipment } from "@/features/shipments/types";
import type { ScanModeId } from "./scanSessionReducer";
import { scanErrorMessage } from "./scanErrors";

export type ScanContext = { kind: "none" } | { kind: "manifest"; manifest: Manifest } | { kind: "rider"; rider: Rider };
export type ScanOutcome = { ok: true; awb: string; summary: string } | { ok: false; awb: string; message: string };
type Can = (permission: string) => boolean;

export interface ScanMode {
  id: ScanModeId;
  label: string;
  title(ctx: ScanContext): string;
  context: "none" | "optional-manifest" | "manifest" | "rider";
  isAvailable(can: Can): boolean;
  requiresContext(can: Can): boolean;
  manifestFilter?(m: Manifest, inScope: (branchId: string) => boolean): boolean;
  submit?(awb: string, ctx: ScanContext): Promise<ScanOutcome>;
}

const rs = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });
const post = (body: unknown): RequestInit => ({ method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });

function describe(s: Pick<Shipment, "recipientName" | "codAmount">): string {
  return `${s.recipientName} · ${s.codAmount > 0 ? `COD Rs ${rs.format(s.codAmount)}` : "Prepaid"}`;
}

async function run(awb: string, op: () => Promise<string>): Promise<ScanOutcome> {
  try {
    return { ok: true, awb, summary: await op() };
  } catch (error) {
    return { ok: false, awb, message: scanErrorMessage(error) };
  }
}

const lookup = (awb: string) => clientFetch<Shipment>(`/api/shipments/${encodeURIComponent(awb)}`);
const manifestOf = (ctx: ScanContext) => (ctx.kind === "manifest" ? ctx.manifest : null);

export const SCAN_MODES: ScanMode[] = [
  {
    id: "receive",
    label: "Receive",
    title: (ctx) => (ctx.kind === "manifest" ? "Receive manifest" : "Receive at origin"),
    context: "optional-manifest",
    isAvailable: (can) => can("shipment.scan") || can("manifest.receive"),
    requiresContext: (can) => !can("shipment.scan"),
    manifestFilter: (m, inScope) => m.status === "InTransit" && inScope(m.toBranchId),
    submit: (awb, ctx) =>
      run(awb, async () => {
        const manifest = manifestOf(ctx);
        if (manifest) {
          await clientFetch(`/api/manifests/${manifest.id}/receive-scan`, post({ awb }));
          return `Received on ${manifest.code}`;
        }
        const shipment = await lookup(awb);
        await clientFetch(`/api/shipments/${shipment.id}/receive-origin`, { method: "POST" });
        return `Received at origin · ${describe(shipment)}`;
      }),
  },
  {
    id: "sort",
    label: "Sort",
    title: () => "Sort",
    context: "none",
    isAvailable: (can) => can("shipment.scan") || can("manifest.create") || can("manifest.receive") || can("delivery.assign"),
    requiresContext: () => false,
  },
  {
    id: "dispatch",
    label: "Dispatch",
    title: () => "Dispatch",
    context: "manifest",
    isAvailable: (can) => can("manifest.create") && can("manifest.read"),
    requiresContext: () => true,
    manifestFilter: (m, inScope) => m.status === "Draft" && m.purpose === "Forward" && inScope(m.fromBranchId),
    submit: (awb, ctx) =>
      run(awb, async () => {
        const manifest = manifestOf(ctx)!;
        await clientFetch(`/api/manifests/${manifest.id}/scan`, post({ awb }));
        return `Added to ${manifest.code}`;
      }),
  },
  {
    id: "handover",
    label: "Rider handover",
    title: () => "Rider handover",
    context: "rider",
    isAvailable: (can) => can("delivery.assign") && can("rider.read"),
    requiresContext: () => true,
    submit: (awb, ctx) =>
      run(awb, async () => {
        if (ctx.kind !== "rider") throw new Error("No rider selected");
        const shipment = await lookup(awb);
        await clientFetch(`/api/riders/tasks`, post({ riderId: ctx.rider.id, shipmentId: shipment.id, type: "Delivery" }));
        return `Out for delivery · ${describe(shipment)}`;
      }),
  },
  {
    id: "rto",
    label: "RTO",
    title: () => "RTO",
    context: "manifest",
    isAvailable: (can) => can("manifest.create") && can("manifest.read"),
    requiresContext: () => true,
    manifestFilter: (m, inScope) => m.status === "Draft" && m.purpose === "Return" && inScope(m.fromBranchId),
    submit: (awb, ctx) =>
      run(awb, async () => {
        const manifest = manifestOf(ctx)!;
        await clientFetch(`/api/manifests/${manifest.id}/rto-scan`, post({ awb }));
        return `Added to return manifest ${manifest.code}`;
      }),
  },
];

export function getScanMode(id: string | null, can: Can): ScanMode | undefined {
  const available = SCAN_MODES.filter((m) => m.isAvailable(can));
  return available.find((m) => m.id === id) ?? available[0];
}
