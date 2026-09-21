import { SCAN_CHECKS, SCAN_OPERATIONS } from "../content";
import { ManifestReconcileDiagram } from "./ManifestReconcileDiagram";
import { MonoLabel } from "./MonoLabel";
import { SectionShell } from "./SectionShell";

export function ScanSection() {
  return (
    <SectionShell
      id="scan-manifest"
      index={2}
      eyebrow="Scan & manifest"
      title="Hubs receive what was sent — and see what wasn't."
      lede="Sealed manifests carry shipments branch to branch. Receiving means scanning each one, so a missing parcel is flagged the moment the truck is unloaded."
    >
      <div className="grid gap-12 lg:grid-cols-2 lg:items-start">
        <div className="flex flex-col gap-8">
          <div className="flex flex-col gap-3">
            <MonoLabel>Scan operations</MonoLabel>
            <ul className="flex flex-wrap gap-2">
              {SCAN_OPERATIONS.map((operation) => (
                <li
                  key={operation}
                  className="rounded-sm border border-marketing-border px-3 py-1.5 font-mono text-xs uppercase tracking-[0.1em] text-marketing-fg transition-colors hover:border-marketing-accent"
                >
                  {operation}
                </li>
              ))}
            </ul>
          </div>
          <div className="flex flex-col gap-3">
            <MonoLabel>Every scan validates</MonoLabel>
            <ul className="flex flex-col divide-y divide-marketing-border/60 border-y border-marketing-border/60">
              {SCAN_CHECKS.map((check) => (
                <li key={check} className="py-2.5 text-sm text-marketing-fg">
                  {check}
                </li>
              ))}
            </ul>
          </div>
        </div>
        <ManifestReconcileDiagram />
      </div>
    </SectionShell>
  );
}
