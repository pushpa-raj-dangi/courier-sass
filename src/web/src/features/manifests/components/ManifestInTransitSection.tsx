"use client";

import { useState } from "react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { FieldError } from "@/components/ui/field-error";
import { useReceiveScanManifestShipment } from "@/features/manifests/hooks/useReceiveScanManifestShipment";
import { manifestScanErrorMessage } from "@/features/manifests/lib/manifestErrorMessages";
import type { Manifest, ManifestShipmentScan } from "@/features/manifests/types";

function parseScanError(error: unknown): string {
  try {
    const parsed = JSON.parse((error as Error).message);
    const reason = parsed?.error?.reason;
    if (typeof reason === "string") {
      return manifestScanErrorMessage(reason);
    }
  } catch {
    // fall through to generic message
  }
  return "Couldn't scan this shipment. Check the AWB and try again.";
}

export function ManifestInTransitSection({ manifest }: { manifest: Manifest }) {
  const [awbInput, setAwbInput] = useState("");
  const [scanned, setScanned] = useState<ManifestShipmentScan[]>([]);
  const [scanError, setScanError] = useState<string | null>(null);
  const receiveScan = useReceiveScanManifestShipment(manifest.id);

  async function handleScan() {
    if (!awbInput.trim()) return;
    setScanError(null);
    try {
      const result = await receiveScan.mutateAsync(awbInput.trim());
      setScanned((prev) => [result, ...prev]);
      setAwbInput("");
    } catch (error) {
      setScanError(parseScanError(error));
    }
  }

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="receive-scan-awb">Receive Scan AWB</Label>
        <Input
          id="receive-scan-awb"
          aria-label="Receive scan AWB"
          aria-invalid={!!scanError}
          aria-describedby={scanError ? "manifest-in-transit-scan-error" : undefined}
          value={awbInput}
          onChange={(e) => setAwbInput(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              handleScan();
            }
          }}
          placeholder="Scan or type an AWB, then press Enter"
        />
        {scanError && <FieldError id="manifest-in-transit-scan-error">{scanError}</FieldError>}
      </div>
      {scanned.length > 0 && (
        <ul className="space-y-1 text-sm">
          {scanned.map((s) => (
            <li key={s.id} className="font-mono">{s.awb}</li>
          ))}
        </ul>
      )}
      <p className="text-xs text-muted-foreground">
        This manifest moves to Received once every scanned shipment has been received, or you can
        close it with missing shipments flagged.
      </p>
    </div>
  );
}
