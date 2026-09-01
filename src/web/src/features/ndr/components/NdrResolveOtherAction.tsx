"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { useResolveNdr } from "@/features/ndr/hooks/useResolveNdr";
import type { Ndr } from "@/features/ndr/types";

// No note field: ResolveNdrRequest.RtoReason is the only free-text field the backend accepts on
// resolve, and NdrService.ResolveAsync's Other branch never reads or persists it (no NDR column
// exists to hold an "Other" note) — building an input for a value the backend silently discards
// would be misleading, not thorough.
export function NdrResolveOtherAction({ ndr }: { ndr: Ndr }) {
  const resolveNdr = useResolveNdr(ndr.id);
  const [serverError, setServerError] = useState<string | null>(null);

  async function handleResolve() {
    setServerError(null);
    try {
      await resolveNdr.mutateAsync({ resolution: "Other" });
    } catch {
      setServerError("Couldn't resolve this NDR. Try again.");
    }
  }

  return (
    <div className="space-y-2">
      {serverError && <p className="text-sm text-destructive">{serverError}</p>}
      <Button type="button" variant="ghost" size="sm" disabled={resolveNdr.isPending} onClick={handleResolve}>
        {resolveNdr.isPending ? <Spinner className="h-4 w-4" /> : "Resolve — Other"}
      </Button>
    </div>
  );
}
