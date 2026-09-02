"use client";

import { useState } from "react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { useScanManifestShipment } from "@/features/manifests/hooks/useScanManifestShipment";
import { useSealManifest } from "@/features/manifests/hooks/useSealManifest";
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

export function ManifestDraftSection({ manifest }: { manifest: Manifest }) {
  const [awbInput, setAwbInput] = useState("");
  const [scanned, setScanned] = useState<ManifestShipmentScan[]>([]);
  const [scanError, setScanError] = useState<string | null>(null);
  const scanShipment = useScanManifestShipment(manifest.id, manifest.purpose);
  const sealManifest = useSealManifest(manifest.id);

  async function handleScan() {
    if (!awbInput.trim()) return;
    setScanError(null);
    try {
      const result = await scanShipment.mutateAsync(awbInput.trim());
      setScanned((prev) => [result, ...prev]);
      setAwbInput("");
    } catch (error) {
      setScanError(parseScanError(error));
    }
  }

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="scan-awb">Scan AWB</Label>
        <Input
          id="scan-awb"
          aria-label="Scan AWB"
          aria-invalid={!!scanError}
          aria-describedby={scanError ? "manifest-draft-scan-error" : undefined}
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
        {scanError && <FieldError id="manifest-draft-scan-error">{scanError}</FieldError>}
      </div>
      {scanned.length > 0 && (
        <ul className="space-y-1 text-sm">
          {scanned.map((s) => (
            <li key={s.id}>{s.awb}</li>
          ))}
        </ul>
      )}
      <Button
        type="button"
        disabled={manifest.shipmentCount === 0 || sealManifest.isPending}
        onClick={() => sealManifest.mutate()}
      >
        {sealManifest.isPending ? <Spinner className="h-4 w-4" /> : "Seal Manifest"}
      </Button>
    </div>
  );
}
