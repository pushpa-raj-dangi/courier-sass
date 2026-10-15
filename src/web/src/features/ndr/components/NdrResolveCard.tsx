"use client";

import { useState } from "react";
import { CheckCircle2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { FieldError } from "@/components/ui/field-error";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { Textarea } from "@/components/ui/textarea";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { SectionCard } from "@/components/page/SectionCard";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useResolveNdr } from "@/features/ndr/hooks/useResolveNdr";
import { cn } from "@/lib/utils";
import type { Ndr, NdrStatus } from "@/features/ndr/types";

type Choice = "Reattempt" | "Rto" | "Other";

// NdrService.ResolveAsync: Reattempt only from ReattemptScheduled, Rto only from RtoRequested,
// Other from any open state. Unavailable choices stay visible, disabled, and say why.
const CHOICES: { key: Choice; label: string; description: string; requires?: NdrStatus; unavailable?: string }[] = [
  { key: "Reattempt", label: "Reattempt", description: "Send the parcel out for delivery again.", requires: "ReattemptScheduled", unavailable: "Available once the NDR is Reattempt scheduled." },
  { key: "Rto", label: "Return to origin", description: "Send the parcel back to the merchant.", requires: "RtoRequested", unavailable: "Available once the NDR is RTO requested." },
  { key: "Other", label: "Resolve other", description: "Close the NDR with no parcel movement." },
];

const SUBMIT_LABEL: Record<Choice, string> = { Reattempt: "Schedule reattempt", Rto: "Start return", Other: "Resolve" };

function defaultChoice(status: NdrStatus): Choice {
  if (status === "ReattemptScheduled") return "Reattempt";
  if (status === "RtoRequested") return "Rto";
  return "Other";
}

export function NdrResolveCard({ ndr, onResolved }: { ndr: Ndr; onResolved: () => void }) {
  const canResolve = usePermission("ndr.resolve");
  const resolveNdr = useResolveNdr(ndr.id);
  const [choice, setChoice] = useState<Choice>(defaultChoice(ndr.status));
  const [rtoReason, setRtoReason] = useState("");
  const [reasonError, setReasonError] = useState<string | null>(null);
  const [serverError, setServerError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);
  if (!canResolve) return null;

  async function submit() {
    setServerError(null);
    try {
      await resolveNdr.mutateAsync({ resolution: choice, rtoReason: choice === "Rto" ? rtoReason.trim() : undefined });
      onResolved();
    } catch {
      setServerError("Couldn't resolve this NDR. Try again.");
    }
  }

  function onPrimary() {
    if (choice === "Rto" && !rtoReason.trim()) {
      setReasonError("Give a reason for the return.");
      return;
    }
    setReasonError(null);
    if (choice === "Reattempt") void submit();
    else setConfirming(true);
  }

  return (
    <SectionCard title="Resolve" icon={CheckCircle2}>
      <div className="space-y-4">
        <fieldset className="space-y-2">
          <legend className="sr-only">Resolution</legend>
          {CHOICES.map((c) => {
            const enabled = !c.requires || c.requires === ndr.status;
            const checked = choice === c.key;
            return (
              <label
                key={c.key}
                className={cn(
                  "flex gap-3 rounded-lg border p-3 focus-within:ring-2 focus-within:ring-ring",
                  checked ? "border-primary bg-accent" : "border-border",
                  enabled ? "cursor-pointer" : "cursor-not-allowed opacity-60",
                )}
              >
                <input
                  type="radio"
                  name={`ndr-resolution-${ndr.id}`}
                  value={c.key}
                  checked={checked}
                  disabled={!enabled}
                  onChange={() => setChoice(c.key)}
                  className="mt-0.5 h-4 w-4 accent-primary"
                />
                <span className="space-y-0.5">
                  <span className="block text-sm font-medium">{c.label}</span>
                  <span className="block text-xs text-muted-foreground">{enabled ? c.description : c.unavailable}</span>
                </span>
              </label>
            );
          })}
        </fieldset>

        {choice === "Reattempt" && (
          <p data-coming-soon="" className="text-xs text-muted-foreground">
            The follow-up date is the scheduling note; booking a dated reattempt needs backend support.
          </p>
        )}

        {choice === "Rto" && (
          <div className="space-y-2">
            <Label htmlFor="ndr-rto-reason">Reason for the return</Label>
            <Textarea
              id="ndr-rto-reason"
              value={rtoReason}
              onChange={(e) => setRtoReason(e.target.value)}
              aria-invalid={!!reasonError}
              aria-describedby={reasonError ? "ndr-rto-reason-error" : undefined}
            />
            {reasonError && <FieldError id="ndr-rto-reason-error">{reasonError}</FieldError>}
          </div>
        )}

        {serverError && <FieldError id="ndr-resolve-server-error">{serverError}</FieldError>}
        <Button type="button" onClick={onPrimary} disabled={resolveNdr.isPending}>
          {resolveNdr.isPending ? <Spinner className="h-4 w-4" /> : SUBMIT_LABEL[choice]}
        </Button>
      </div>

      <AlertDialog open={confirming} onOpenChange={setConfirming}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{choice === "Rto" ? `Start the return for ${ndr.awb}?` : `Resolve ${ndr.awb} with no parcel movement?`}</AlertDialogTitle>
            <AlertDialogDescription>
              {choice === "Rto"
                ? "The parcel heads back to the origin branch and a return shipment is created."
                : "The NDR closes. Nothing moves the parcel."}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Go back</AlertDialogCancel>
            <AlertDialogAction onClick={() => void submit()}>{SUBMIT_LABEL[choice]}</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </SectionCard>
  );
}
