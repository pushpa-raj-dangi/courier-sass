"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { useResolveNdr } from "@/features/ndr/hooks/useResolveNdr";
import type { Ndr } from "@/features/ndr/types";

export function NdrReattemptSection({ ndr }: { ndr: Ndr }) {
  const resolveNdr = useResolveNdr(ndr.id);
  const [serverError, setServerError] = useState<string | null>(null);

  async function handleResolve() {
    setServerError(null);
    try {
      await resolveNdr.mutateAsync({ resolution: "Reattempt" });
    } catch {
      setServerError("Couldn't resolve this NDR. Try again.");
    }
  }

  return (
    <div className="space-y-4">
      <p className="text-sm text-muted-foreground">
        Resolving sends this shipment back to Out for Delivery for another attempt.
      </p>
      {serverError && <FieldError id="ndr-reattempt-server-error">{serverError}</FieldError>}
      <Button type="button" disabled={resolveNdr.isPending} onClick={handleResolve}>
        {resolveNdr.isPending ? <Spinner className="h-4 w-4" /> : "Resolve — Reattempt Delivery"}
      </Button>
    </div>
  );
}
