"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { DateTimePicker } from "@/components/ui/date-time-picker";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { useUpdateNdr } from "@/features/ndr/hooks/useUpdateNdr";
import type { Ndr, NdrStatus } from "@/features/ndr/types";

// The one real branch point in NdrService.IsLegalForwardMove: CustomerConfirmed can move to either
// ReattemptScheduled or RtoRequested. Every other status has exactly one legal forward move.
const NEXT_STATUSES: Record<string, { status: NdrStatus; label: string }[]> = {
  Open: [{ status: "Contacting", label: "Move to Contacting" }],
  Contacting: [{ status: "CustomerConfirmed", label: "Move to Customer Confirmed" }],
  CustomerConfirmed: [
    { status: "ReattemptScheduled", label: "Schedule Reattempt" },
    { status: "RtoRequested", label: "Request RTO" },
  ],
};

export function NdrUpdateSection({ ndr }: { ndr: Ndr }) {
  const [customerResponse, setCustomerResponse] = useState("");
  const [nextAction, setNextAction] = useState("");
  const [followUpAt, setFollowUpAt] = useState("");
  const updateNdr = useUpdateNdr(ndr.id);
  const [serverError, setServerError] = useState<string | null>(null);

  const moves = NEXT_STATUSES[ndr.status] ?? [];

  async function handleMove(status: NdrStatus) {
    setServerError(null);
    try {
      await updateNdr.mutateAsync({
        status,
        customerResponse: customerResponse || undefined,
        nextAction: nextAction || undefined,
        followUpAt: followUpAt ? new Date(followUpAt).toISOString() : undefined,
      });
    } catch {
      setServerError("Couldn't update this NDR. Check the fields and try again.");
    }
  }

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="ndr-customer-response">Customer Response</Label>
        <Textarea id="ndr-customer-response" value={customerResponse} onChange={(e) => setCustomerResponse(e.target.value)} />
      </div>
      <div className="space-y-2">
        <Label htmlFor="ndr-next-action">Next Action</Label>
        <Input id="ndr-next-action" value={nextAction} onChange={(e) => setNextAction(e.target.value)} />
      </div>
      <div className="space-y-2">
        <Label htmlFor="ndr-follow-up">Follow Up At</Label>
        <DateTimePicker id="ndr-follow-up" value={followUpAt} onChange={setFollowUpAt} clearable />
      </div>
      {serverError && <FieldError id="ndr-update-server-error">{serverError}</FieldError>}
      <div className="flex gap-2">
        {moves.map((move) => (
          <Button key={move.status} type="button" disabled={updateNdr.isPending} onClick={() => handleMove(move.status)}>
            {updateNdr.isPending ? <Spinner className="h-4 w-4" /> : move.label}
          </Button>
        ))}
      </div>
    </div>
  );
}
