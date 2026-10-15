"use client";

import { useEffect, useRef, useState } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useQueryClient } from "@tanstack/react-query";
import { Volume2, VolumeX } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { PageHeader } from "@/components/page/PageHeader";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";
import { EmptyState } from "@/components/states/EmptyState";
import { BarcodeScanner } from "@/components/scanner/BarcodeScanner";
import { useSession } from "@/features/auth/hooks/useSession";
import { useManifestPanelStore } from "@/features/manifests/store/manifestPanelStore";
import { useManifest } from "@/features/manifests/hooks/useManifest";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { SCAN_MODES, getScanMode, type ScanContext, type ScanOutcome } from "@/features/scanning/modes";
import { useScanSession } from "@/features/scanning/useScanSession";
import { playScanTone } from "@/features/scanning/beep";
import { ManualScanInput } from "./ManualScanInput";
import { ScanResultCard } from "./ScanResultCard";
import { ScanLog } from "./ScanLog";
import { ScanContextPicker } from "./ScanContextPicker";
import { ReconciliationPanel } from "./ReconciliationPanel";

const SOUND_KEY = "scan-station-sound";

function readSound(): boolean {
  try {
    return localStorage.getItem(SOUND_KEY) !== "off";
  } catch {
    return true;
  }
}

export function ScanStation() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const queryClient = useQueryClient();
  const { data: session } = useSession();
  const names = usePeopleNames();
  const openManifest = useManifestPanelStore((s) => s.openDetail);

  const granted = session?.permissions ?? [];
  const can = (p: string) => granted.includes(p);
  const inScope = (branchId: string) => Boolean(session?.hasAllBranchesScope || session?.branches.some((b) => b.branchId === branchId));
  const mode = getScanMode(searchParams.get("mode"), can);

  const [context, setContext] = useState<ScanContext>({ kind: "none" });
  const [sound, setSound] = useState(true);
  const inputRef = useRef<HTMLInputElement>(null);
  useEffect(() => setSound(readSound()), []);
  useEffect(() => setContext({ kind: "none" }), [mode?.id]);

  const contextManifestId = context.kind === "manifest" ? context.manifest.id : "";
  const liveManifest = useManifest(contextManifestId).data;

  const soundRef = useRef(sound);
  soundRef.current = sound;
  const onSettled = (outcome: ScanOutcome) => {
    if (soundRef.current) playScanTone(outcome.ok);
    if (outcome.ok) {
      void queryClient.invalidateQueries({ queryKey: ["manifests"] });
      void queryClient.invalidateQueries({ queryKey: ["manifests-summary"] });
      void queryClient.invalidateQueries({ queryKey: ["shipments"] });
      // Not under the ["shipments"] prefix — the dashboard pipeline counts.
      void queryClient.invalidateQueries({ queryKey: ["shipments-summary"] });
    }
    inputRef.current?.focus();
  };
  const { state, request } = useScanSession(mode, context, onSettled);

  if (!mode) {
    return <EmptyState title="You don't have permission to scan parcels." description="Ask an admin for scan, manifest or delivery-assign access." />;
  }

  const missingContext = mode.requiresContext(can) && context.kind === "none";
  const canScan = Boolean(mode.submit) && !missingContext;
  const manifest = context.kind === "manifest" ? (liveManifest ?? context.manifest) : null;

  function setMode(id: string) {
    router.replace(`${pathname}?mode=${id}`, { scroll: false });
  }
  function toggleSound() {
    const next = !sound;
    setSound(next);
    try {
      localStorage.setItem(SOUND_KEY, next ? "on" : "off");
    } catch {
      // preference is best-effort
    }
  }

  const chips: string[] = manifest
    ? [manifest.code, `${names.branchName(manifest.fromBranchId)} → ${names.branchName(manifest.toBranchId)}`, ...(manifest.vehiclePlate ? [`Vehicle ${manifest.vehiclePlate}`] : [])]
    : context.kind === "rider"
      ? [names.riderName(context.rider.id)]
      : [];

  return (
    <div className="space-y-5">
      <PageHeader
        title={mode.title(context)}
        badge={chips.length > 0 && (
          <span className="flex flex-wrap gap-1.5">
            {chips.map((c) => (
              <span key={c} className="rounded-md border border-border bg-card px-2.5 py-1 text-xs">{c}</span>
            ))}
          </span>
        )}
        actions={
          <>
            <Tabs value={mode.id} onValueChange={setMode}>
              <TabsList aria-label="Scan mode" className="flex-wrap">
                {SCAN_MODES.filter((m) => m.isAvailable(can)).map((m) => (
                  <TabsTrigger key={m.id} value={m.id}>{m.label}</TabsTrigger>
                ))}
              </TabsList>
            </Tabs>
            <Button type="button" variant="ghost" size="icon" aria-label={sound ? "Mute scan sounds" : "Unmute scan sounds"} aria-pressed={sound} onClick={toggleSound}>
              {sound ? <Volume2 className="h-4 w-4" aria-hidden="true" /> : <VolumeX className="h-4 w-4" aria-hidden="true" />}
            </Button>
          </>
        }
      />

      {!mode.submit ? (
        <ComingSoonSlot
          title="Sorting"
          description="Sorting happens when parcels are scanned onto a draft manifest — standalone sort and bins need backend."
          minHeight={240}
        />
      ) : (
        <>
          {mode.context !== "none" && (
            <div className="flex flex-col gap-1.5">
              <ScanContextPicker mode={mode} value={context} onChange={setContext} inScope={inScope} />
              {missingContext && (
                <p className="text-sm text-muted-foreground">
                  {mode.context === "rider" ? "Choose a rider to start scanning." : "Choose a manifest to start scanning."}
                </p>
              )}
            </div>
          )}
          <div className="grid grid-cols-1 gap-5 lg:grid-cols-[minmax(0,1fr)_380px]">
            <div className="flex min-w-0 flex-col gap-4">
              {canScan && <BarcodeScanner continuous manualEntry={false} onDetect={request} />}
              <ManualScanInput ref={inputRef} disabled={!canScan} onSubmit={request} />
              <ScanResultCard entry={state.last} />
              <ScanLog entries={state.log} />
            </div>
            <div className="flex flex-col gap-4">
              {mode.id === "receive" && manifest && <ReconciliationPanel manifest={manifest} />}
              {(mode.id === "dispatch" || mode.id === "rto") && manifest && (
                <section aria-label="Manifest" className="space-y-2 rounded-xl border border-border bg-card p-5">
                  <h2 className="font-display text-base font-semibold">{manifest.code}</h2>
                  <p className="text-sm text-muted-foreground">To {names.branchName(manifest.toBranchId)}</p>
                  <p className="text-2xl font-semibold tabular-nums">{manifest.shipmentCount} <span className="text-sm font-normal text-muted-foreground">scanned</span></p>
                  <Button type="button" variant="outline" size="sm" onClick={() => openManifest(manifest.id)}>Open manifest</Button>
                </section>
              )}
              {mode.id === "handover" && context.kind === "rider" && (
                <section aria-label="Rider" className="space-y-1 rounded-xl border border-border bg-card p-5">
                  <h2 className="font-display text-base font-semibold">{names.riderName(context.rider.id)}</h2>
                  <p className="text-sm text-muted-foreground">Base branch · {names.branchName(context.rider.baseBranchId)}</p>
                </section>
              )}
              {(mode.id === "receive" || mode.id === "dispatch") && (
                <ComingSoonSlot title="Sort bins" description="Which bin each parcel goes into, with live bin counts." />
              )}
            </div>
          </div>
        </>
      )}
    </div>
  );
}
