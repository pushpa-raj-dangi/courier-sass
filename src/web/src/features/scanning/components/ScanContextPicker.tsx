"use client";

import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useManifests } from "@/features/manifests/hooks/useManifests";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { usePermission } from "@/features/auth/hooks/usePermission";
import type { ScanContext, ScanMode } from "@/features/scanning/modes";

export function ScanContextPicker({
  mode,
  value,
  onChange,
  inScope,
}: {
  mode: ScanMode;
  value: ScanContext;
  onChange: (ctx: ScanContext) => void;
  inScope: (branchId: string) => boolean;
}) {
  const wantsManifest = mode.context === "manifest" || mode.context === "optional-manifest";
  // Both permission hooks are called unconditionally (rules of hooks).
  const canReadManifests = usePermission("manifest.read");
  const canReceive = usePermission("manifest.receive");
  const canManifests = canReadManifests && (mode.context !== "optional-manifest" || canReceive);
  const manifests = useManifests(undefined, undefined, { enabled: wantsManifest && canManifests });
  const riders = useRiders({ enabled: mode.context === "rider" });
  const names = usePeopleNames();

  if (wantsManifest && canManifests) {
    const options = (manifests.data ?? []).filter((m) => mode.manifestFilter?.(m, inScope));
    const selected = value.kind === "manifest" ? value.manifest.id : "";
    return (
      <Select
        value={selected || (mode.context === "optional-manifest" ? "none" : "")}
        onValueChange={(id) => {
          const m = options.find((o) => o.id === id);
          onChange(m ? { kind: "manifest", manifest: m } : { kind: "none" });
        }}
      >
        <SelectTrigger className="w-full sm:w-96" aria-label="Manifest">
          <SelectValue placeholder={manifests.isLoading ? "Loading manifests…" : "Choose a manifest"} />
        </SelectTrigger>
        <SelectContent>
          {mode.context === "optional-manifest" && <SelectItem value="none">No manifest — receive at origin</SelectItem>}
          {options.map((m) => (
            <SelectItem key={m.id} value={m.id}>
              {`${m.code} · ${names.branchName(m.fromBranchId)} → ${names.branchName(m.toBranchId)}`}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    );
  }

  if (mode.context === "rider") {
    const active = (riders.data ?? []).filter((r) => r.isActive && inScope(r.baseBranchId));
    return (
      <Select
        value={value.kind === "rider" ? value.rider.id : ""}
        onValueChange={(id) => {
          const rider = active.find((r) => r.id === id);
          if (rider) onChange({ kind: "rider", rider });
        }}
      >
        <SelectTrigger className="w-full sm:w-80" aria-label="Rider">
          <SelectValue placeholder={riders.isLoading ? "Loading riders…" : "Choose a rider"} />
        </SelectTrigger>
        <SelectContent>
          {active.map((r) => {
            const name = names.riderName(r.id);
            return (
              <SelectItem key={r.id} value={r.id}>
                {name === "Rider" ? `Rider · ${r.licenseNumber}` : name}
              </SelectItem>
            );
          })}
        </SelectContent>
      </Select>
    );
  }

  return null;
}
