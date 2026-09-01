"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Textarea } from "@/components/ui/textarea";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { useMyDeliveriesPanelStore } from "@/features/riders/store/myDeliveriesPanelStore";
import { useRecordFailedAttempt } from "@/features/riders/hooks/useRecordFailedAttempt";
import type { DeliveryAttemptReason } from "@/features/riders/types";

const FAILURE_REASONS: DeliveryAttemptReason[] = [
  "CustomerUnavailable", "PhoneUnreachable", "WrongAddress", "IncompleteAddress",
  "CustomerRefused", "CustomerRequestedReschedule", "CashUnavailable", "DamagedParcel", "Other",
];

export function RecordAttemptSheet({ riderId }: { riderId: string }) {
  const panel = useMyDeliveriesPanelStore((s) => s.panel);
  const close = useMyDeliveriesPanelStore((s) => s.close);
  const isOpen = panel.mode === "record-attempt";
  const task = panel.mode === "record-attempt" ? panel.task : null;

  const [outcome, setOutcome] = useState<"success" | "failure">("success");
  const [reason, setReason] = useState<DeliveryAttemptReason | undefined>(undefined);
  const [notes, setNotes] = useState("");
  const [recipientName, setRecipientName] = useState("");
  const [otpVerified, setOtpVerified] = useState(false);
  const [collectedCodAmount, setCollectedCodAmount] = useState("");

  const recordFailedAttempt = useRecordFailedAttempt(riderId);
  const [serverError, setServerError] = useState<string | null>(null);

  function reset() {
    setOutcome("success");
    setReason(undefined);
    setNotes("");
    setRecipientName("");
    setOtpVerified(false);
    setCollectedCodAmount("");
    setServerError(null);
  }

  async function handleSubmitFailure() {
    if (!task || !reason) return;
    setServerError(null);
    try {
      await recordFailedAttempt.mutateAsync({
        shipmentId: task.shipmentId,
        reason,
        notes: notes || undefined,
        customerContacted: true,
      });
      reset();
      close();
    } catch {
      setServerError("Couldn't record this attempt. Check the fields and try again.");
    }
  }

  return (
    <Sheet
      open={isOpen}
      onOpenChange={(open) => {
        if (!open) {
          reset();
          close();
        }
      }}
    >
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>Record Delivery Attempt</SheetTitle>
        </SheetHeader>
        {task && (
          <div className="mt-6 space-y-4">
            <div className="flex gap-2">
              <Button
                type="button"
                variant={outcome === "success" ? "default" : "outline"}
                size="sm"
                onClick={() => setOutcome("success")}
              >
                Delivered
              </Button>
              <Button
                type="button"
                variant={outcome === "failure" ? "default" : "outline"}
                size="sm"
                onClick={() => setOutcome("failure")}
              >
                Failed
              </Button>
            </div>

            {outcome === "failure" && (
              <div className="space-y-4">
                <div className="space-y-2">
                  <Label htmlFor="attempt-reason">Reason</Label>
                  <Select value={reason} onValueChange={(v) => setReason(v as DeliveryAttemptReason)}>
                    <SelectTrigger id="attempt-reason" aria-label="Reason">
                      <SelectValue placeholder="Select reason" />
                    </SelectTrigger>
                    <SelectContent>
                      {FAILURE_REASONS.map((r) => (
                        <SelectItem key={r} value={r}>{r}</SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-2">
                  <Label htmlFor="attempt-notes">Notes</Label>
                  <Textarea id="attempt-notes" value={notes} onChange={(e) => setNotes(e.target.value)} />
                </div>
                {serverError && <p className="text-sm text-destructive">{serverError}</p>}
                <Button type="button" disabled={!reason || recordFailedAttempt.isPending} onClick={handleSubmitFailure}>
                  {recordFailedAttempt.isPending ? <Spinner className="h-4 w-4" /> : "Submit"}
                </Button>
              </div>
            )}

            {outcome === "success" && (
              <div className="space-y-4">
                <div className="space-y-2">
                  <Label htmlFor="recipient-name">Recipient Name</Label>
                  <Input id="recipient-name" value={recipientName} onChange={(e) => setRecipientName(e.target.value)} />
                </div>
                <div className="flex items-center gap-2">
                  <Checkbox id="otp-verified" checked={otpVerified} onCheckedChange={(v) => setOtpVerified(v === true)} />
                  <Label htmlFor="otp-verified">Customer verified via OTP</Label>
                </div>
                {task.codAmount > 0 && (
                  <div className="space-y-2">
                    <Label htmlFor="collected-cod">Collected COD Amount</Label>
                    <Input
                      id="collected-cod"
                      type="number"
                      value={collectedCodAmount}
                      onChange={(e) => setCollectedCodAmount(e.target.value)}
                      placeholder={String(task.codAmount)}
                    />
                  </div>
                )}
              </div>
            )}
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
