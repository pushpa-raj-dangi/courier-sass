"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { useResolveNdr } from "@/features/ndr/hooks/useResolveNdr";
import type { Ndr } from "@/features/ndr/types";

export function NdrRtoSection({ ndr }: { ndr: Ndr }) {
  const [rtoReason, setRtoReason] = useState("");
  const resolveNdr = useResolveNdr(ndr.id);
  const [serverError, setServerError] = useState<string | null>(null);

  async function handleResolve() {
    if (!rtoReason.trim()) return;
    setServerError(null);
    try {
      await resolveNdr.mutateAsync({ resolution: "Rto", rtoReason: rtoReason.trim() });
    } catch {
      setServerError("Couldn't resolve this NDR. Try again.");
    }
  }

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="ndr-rto-reason">RTO Reason</Label>
        <Textarea id="ndr-rto-reason" value={rtoReason} onChange={(e) => setRtoReason(e.target.value)} />
      </div>
      {serverError && <FieldError id="ndr-rto-server-error">{serverError}</FieldError>}
      <Button type="button" disabled={!rtoReason.trim() || resolveNdr.isPending} onClick={handleResolve}>
        {resolveNdr.isPending ? <Spinner className="h-4 w-4" /> : "Resolve — Initiate RTO"}
      </Button>
    </div>
  );
}
