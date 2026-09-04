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
import { FieldError } from "@/components/ui/field-error";
import { SignaturePad } from "@/features/riders/components/SignaturePad";
import { useRiderAppPanelStore } from "@/features/rider-app/store/riderAppPanelStore";
import { useRecordFailedAttempt } from "@/features/rider-app/hooks/useRecordFailedAttempt";
import { useCompleteDelivery } from "@/features/rider-app/hooks/useCompleteDelivery";
import type { RecordFailedAttemptFields } from "@/features/rider-app/sync/types";

const FAILURE_REASONS: RecordFailedAttemptFields["reason"][] = [
  "CustomerUnavailable", "PhoneUnreachable", "WrongAddress", "IncompleteAddress",
  "CustomerRefused", "CustomerRequestedReschedule", "CashUnavailable", "DamagedParcel", "Other",
];

export function RiderRecordAttemptSheet({ riderId }: { riderId: string }) {
  const panel = useRiderAppPanelStore((s) => s.panel);
  const close = useRiderAppPanelStore((s) => s.close);
  const isOpen = panel.mode === "record-attempt";
  const task = panel.mode === "record-attempt" ? panel.task : null;

  const [outcome, setOutcome] = useState<"success" | "failure">("success");
  const [reason, setReason] = useState<RecordFailedAttemptFields["reason"] | undefined>(undefined);
  const [notes, setNotes] = useState("");
  const [recipientName, setRecipientName] = useState("");
  const [otpVerified, setOtpVerified] = useState(false);
  const [collectedCodAmount, setCollectedCodAmount] = useState("");
  const [signatureBlob, setSignatureBlob] = useState<Blob | null>(null);
  const [photoFile, setPhotoFile] = useState<File | null>(null);
  const [latitude, setLatitude] = useState<number | null>(null);
  const [longitude, setLongitude] = useState<number | null>(null);
  const [serverError, setServerError] = useState<string | null>(null);

  const recordFailedAttempt = useRecordFailedAttempt(riderId);
  const completeDelivery = useCompleteDelivery(riderId);

  function reset() {
    setOutcome("success");
    setReason(undefined);
    setNotes("");
    setRecipientName("");
    setOtpVerified(false);
    setCollectedCodAmount("");
    setSignatureBlob(null);
    setPhotoFile(null);
    setLatitude(null);
    setLongitude(null);
    setServerError(null);
  }

  function captureLocation() {
    if (!("geolocation" in navigator)) return;
    navigator.geolocation.getCurrentPosition(
      (position) => {
        setLatitude(position.coords.latitude);
        setLongitude(position.coords.longitude);
      },
      () => {}
    );
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
      setServerError("Couldn't queue this attempt. Try again.");
    }
  }

  const codRequired = task ? task.codAmount > 0 : false;
  const codInvalid = codRequired && (collectedCodAmount === "" || Number(collectedCodAmount) < 0);
  const canSubmitSuccess = recipientName.trim().length > 0 && !codInvalid;

  async function handleSubmitSuccess() {
    if (!task || !canSubmitSuccess) return;
    setServerError(null);
    try {
      await completeDelivery.mutateAsync({
        shipmentId: task.shipmentId,
        recipientName: recipientName.trim(),
        otpVerified,
        latitude,
        longitude,
        collectedCodAmount: codRequired ? Number(collectedCodAmount) : null,
        signatureBlob,
        photoBlob: photoFile,
      });
      reset();
      close();
    } catch {
      setServerError("Couldn't queue this delivery. Try again.");
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) { reset(); close(); } }}>
      <SheetContent side="bottom" className="max-h-[90vh] overflow-y-auto rounded-t-2xl">
        <SheetHeader>
          <SheetTitle>Record Delivery Attempt</SheetTitle>
        </SheetHeader>
        {task && (
          <div className="mt-6 space-y-4">
            <div className="flex gap-2">
              <Button type="button" variant={outcome === "success" ? "default" : "outline"} size="lg" className="flex-1" onClick={() => setOutcome("success")}>
                Delivered
              </Button>
              <Button type="button" variant={outcome === "failure" ? "default" : "outline"} size="lg" className="flex-1" onClick={() => setOutcome("failure")}>
                Failed
              </Button>
            </div>

            {outcome === "failure" && (
              <div className="space-y-4">
                <div className="space-y-2">
                  <Label htmlFor="attempt-reason">Reason</Label>
                  <Select value={reason} onValueChange={(v) => setReason(v as RecordFailedAttemptFields["reason"])}>
                    <SelectTrigger id="attempt-reason" aria-label="Reason">
                      <SelectValue placeholder="Select reason" />
                    </SelectTrigger>
                    <SelectContent>
                      {FAILURE_REASONS.map((r) => <SelectItem key={r} value={r}>{r}</SelectItem>)}
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-2">
                  <Label htmlFor="attempt-notes">Notes</Label>
                  <Textarea id="attempt-notes" value={notes} onChange={(e) => setNotes(e.target.value)} />
                </div>
                {serverError && <FieldError id="record-attempt-server-error">{serverError}</FieldError>}
                <Button type="button" size="lg" className="w-full" disabled={!reason || recordFailedAttempt.isPending} onClick={handleSubmitFailure}>
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
                <div className="space-y-2">
                  <Label>Signature</Label>
                  <SignaturePad onChange={setSignatureBlob} />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="pod-photo">Photo</Label>
                  <input id="pod-photo" type="file" accept="image/*" capture="environment" onChange={(e) => setPhotoFile(e.target.files?.[0] ?? null)} />
                </div>
                <Button type="button" variant="outline" size="sm" onClick={captureLocation}>
                  {latitude !== null ? "Location captured" : "Capture current location"}
                </Button>
                {task.codAmount > 0 && (
                  <div className="space-y-2">
                    <Label htmlFor="collected-cod">Collected COD Amount</Label>
                    <Input id="collected-cod" type="number" value={collectedCodAmount} onChange={(e) => setCollectedCodAmount(e.target.value)} placeholder={String(task.codAmount)} />
                  </div>
                )}
                {serverError && <FieldError id="record-attempt-server-error">{serverError}</FieldError>}
                <Button type="button" size="lg" className="w-full" disabled={!canSubmitSuccess || completeDelivery.isPending} onClick={handleSubmitSuccess}>
                  {completeDelivery.isPending ? <Spinner className="h-4 w-4" /> : "Submit"}
                </Button>
              </div>
            )}
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
