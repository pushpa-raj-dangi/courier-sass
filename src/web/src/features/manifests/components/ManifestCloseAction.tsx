"use client";

import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle, AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useCloseManifest } from "@/features/manifests/hooks/useCloseManifest";
import type { Manifest } from "@/features/manifests/types";

// The backend allows closing with parcels still unscanned (they stay listed as missing), so the
// missing case confirms first; a fully received manifest closes in one click.
export function ManifestCloseAction({ manifest }: { manifest: Manifest }) {
  const canClose = usePermission("manifest.close");
  const close = useCloseManifest(manifest.id);
  if (!canClose) return null;

  const missingCount = Math.max(0, manifest.shipmentCount - manifest.receivedCount);
  const label = missingCount > 0 ? `Close with ${missingCount} missing` : "Close manifest";

  return (
    <div className="mt-auto space-y-2 border-t border-border pt-4">
      {missingCount === 0 ? (
        <Button type="button" className="w-full" disabled={close.isPending} onClick={() => close.mutate()}>
          {close.isPending ? <Spinner className="h-4 w-4" /> : label}
        </Button>
      ) : (
        <AlertDialog>
          <AlertDialogTrigger asChild>
            <Button type="button" className="w-full" disabled={close.isPending}>
              {close.isPending ? <Spinner className="h-4 w-4" /> : label}
            </Button>
          </AlertDialogTrigger>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>{`Close ${manifest.code}?`}</AlertDialogTitle>
              <AlertDialogDescription>{`${missingCount} parcels stay listed as missing.`}</AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel>Keep receiving</AlertDialogCancel>
              <AlertDialogAction onClick={() => close.mutate()}>{label}</AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      )}
      {close.isError && <p role="alert" className="text-sm text-destructive">Couldn&apos;t close this manifest. Try again.</p>}
    </div>
  );
}
