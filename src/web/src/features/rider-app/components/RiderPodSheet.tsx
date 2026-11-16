"use client";

import { useEffect, useState } from "react";
import { ArrowLeft } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { FieldError } from "@/components/ui/field-error";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetDescription, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { Textarea } from "@/components/ui/textarea";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";
import { formatRs } from "@/features/cod/lib/codFormat";
import { SignaturePad } from "@/features/riders/components/SignaturePad";
import { useCompleteDelivery } from "@/features/rider-app/hooks/useCompleteDelivery";
import { useRecordFailedAttempt } from "@/features/rider-app/hooks/useRecordFailedAttempt";
import { FAILURE_REASONS, cashMatch, failureReasonLabel } from "@/features/rider-app/lib/runModel";
import { useHandledStore } from "@/features/rider-app/store/handledStore";
import { useRiderAppPanelStore, type PodStart } from "@/features/rider-app/store/riderAppPanelStore";
import { useOutboxStatusStore } from "@/features/rider-app/sync/outboxStatusStore";
import type { RecordFailedAttemptFields } from "@/features/rider-app/sync/types";
import type { MyTask } from "@/features/riders/types";
import { cn } from "@/lib/utils";

// Backend pod-uploads accepts only image/jpeg, image/png (and pdf), capped at 10MB — the picker
// stays aligned so the outbox never queues a file the server will reject on replay.
const MAX_PHOTO_BYTES = 10 * 1024 * 1024;

type Gps =
  | { status: "pending" }
  | { status: "ok"; latitude: number; longitude: number; accuracy: number }
  | { status: "unavailable" };

function gpsText(gps: Gps): string {
  if (gps.status === "pending") return "Getting location…";
  if (gps.status === "unavailable") return "Location unavailable";
  return `GPS ${gps.latitude.toFixed(4)}, ${gps.longitude.toFixed(4)} ±${Math.round(gps.accuracy)} m`;
}

function StepLabel({ children }: { children: string }) {
  return <p className="font-mono text-2xs uppercase tracking-[0.1em] text-muted-foreground">{children}</p>;
}

const SECTION = "space-y-3 rounded-xl border border-border bg-card p-4";

export function RiderPodSheet({ riderId }: { riderId: string }) {
  const panel = useRiderAppPanelStore((s) => s.panel);
  const close = useRiderAppPanelStore((s) => s.close);
  const isOpen = panel.mode === "record-attempt";

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent side="bottom" className="h-[100dvh] overflow-y-auto p-0">
        {panel.mode === "record-attempt" && (
          // Keyed per task and start mode so every opening starts from a clean form.
          <PodForm key={`${panel.task.taskId}-${panel.start}`} task={panel.task} riderId={riderId} start={panel.start} onDone={close} />
        )}
      </SheetContent>
    </Sheet>
  );
}

function PodForm({ task, riderId, start, onDone }: { task: MyTask; riderId: string; start: PodStart; onDone: () => void }) {
  const [mode, setMode] = useState<PodStart>(start);
  const [cashReceived, setCashReceived] = useState("");
  const [otpVerified, setOtpVerified] = useState(false);
  const [recipientName, setRecipientName] = useState("");
  const [signatureBlob, setSignatureBlob] = useState<Blob | null>(null);
  const [photoFile, setPhotoFile] = useState<File | null>(null);
  const [photoError, setPhotoError] = useState<string | null>(null);
  const [gps, setGps] = useState<Gps>({ status: "pending" });
  const [reason, setReason] = useState<RecordFailedAttemptFields["reason"] | undefined>(undefined);
  const [notes, setNotes] = useState("");
  const [customerContacted, setCustomerContacted] = useState(false);
  const [nextAction, setNextAction] = useState("");
  const [serverError, setServerError] = useState<string | null>(null);

  const completeDelivery = useCompleteDelivery(riderId);
  const recordFailedAttempt = useRecordFailedAttempt(riderId);
  const markHandled = useHandledStore((s) => s.markHandled);

  // Captured once on open; a missing or denied location never blocks the delivery.
  useEffect(() => {
    if (typeof navigator === "undefined" || !("geolocation" in navigator)) {
      setGps({ status: "unavailable" });
      return;
    }
    navigator.geolocation.getCurrentPosition(
      (p) => setGps({ status: "ok", latitude: p.coords.latitude, longitude: p.coords.longitude, accuracy: p.coords.accuracy }),
      () => setGps({ status: "unavailable" }),
      { enableHighAccuracy: true, timeout: 15_000 },
    );
  }, []);

  const codDue = task.codAmount > 0;
  const enteredCash = cashReceived.trim() === "" ? null : Number(cashReceived);
  const cashInvalid = codDue && (enteredCash === null || !Number.isFinite(enteredCash) || enteredCash < 0);
  const match = codDue && !cashInvalid && enteredCash !== null ? cashMatch(task.codAmount, enteredCash) : null;
  const canConfirm = recipientName.trim().length > 0 && !cashInvalid;
  const latitude = gps.status === "ok" ? gps.latitude : null;
  const longitude = gps.status === "ok" ? gps.longitude : null;
  const otpStep = codDue ? 2 : 1;

  async function confirmDelivered() {
    if (!canConfirm) return;
    setServerError(null);
    try {
      await completeDelivery.mutateAsync({
        shipmentId: task.shipmentId,
        recipientName: recipientName.trim(),
        otpVerified,
        latitude,
        longitude,
        collectedCodAmount: codDue ? enteredCash : null,
        signatureBlob,
        photoBlob: photoFile,
      });
      markHandled(task.shipmentId);
      void useOutboxStatusStore.getState().refresh();
      onDone();
    } catch {
      setServerError("Couldn't save this delivery on the phone. Try again.");
    }
  }

  async function recordFailure() {
    if (!reason) return;
    setServerError(null);
    try {
      await recordFailedAttempt.mutateAsync({
        shipmentId: task.shipmentId,
        reason,
        notes: notes.trim() || undefined,
        latitude,
        longitude,
        customerContacted,
        nextAction: nextAction.trim() || undefined,
      });
      markHandled(task.shipmentId);
      void useOutboxStatusStore.getState().refresh();
      onDone();
    } catch {
      setServerError("Couldn't save this attempt on the phone. Try again.");
    }
  }

  return (
    <TooltipProvider>
      <div className="flex min-h-full flex-col bg-background">
        <div className="flex items-center gap-3 border-b border-border bg-card px-4 py-3">
          <Button type="button" variant="outline" size="icon" className="h-11 w-11" aria-label="Back to run" onClick={onDone}>
            <ArrowLeft className="h-4 w-4" aria-hidden="true" />
          </Button>
          <div className="flex flex-col">
            <SheetTitle className="text-base font-semibold">{mode === "deliver" ? "Complete delivery" : "Couldn't deliver"}</SheetTitle>
            <SheetDescription className="font-mono text-xs text-muted-foreground">{`${task.awb} · stop ${task.sequence}`}</SheetDescription>
          </div>
        </div>

        <div className="flex-1 space-y-3 p-4">
          <section aria-label="Recipient" className="space-y-1 rounded-xl border border-border bg-card p-4">
            <p className="text-base font-semibold">{task.recipientName}</p>
            <a href={`tel:${task.recipientPhone}`} className="text-sm font-medium text-primary underline-offset-4 hover:underline">
              {task.recipientPhone}
            </a>
            <p className="text-sm text-muted-foreground">{`${task.recipientAddressLine}, Ward ${task.recipientWardNumber}`}</p>
            <p className="text-xs text-muted-foreground">{gpsText(gps)}</p>
          </section>

          {mode === "deliver" ? (
            <>
              {codDue && (
                <section aria-label="Collect COD" className={SECTION}>
                  <div className="flex items-baseline justify-between">
                    <StepLabel>1 · Collect COD</StepLabel>
                    <span className="text-2xl font-semibold tabular-nums">{formatRs(task.codAmount)}</span>
                  </div>
                  <div className="grid grid-cols-2 gap-2">
                    <Button type="button" variant="secondary" className="h-11" aria-pressed="true">Cash</Button>
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <span tabIndex={0} className="block">
                          <Button type="button" variant="outline" className="h-11 w-full" disabled aria-pressed="false">Fonepay QR</Button>
                        </span>
                      </TooltipTrigger>
                      <TooltipContent>Needs backend</TooltipContent>
                    </Tooltip>
                  </div>
                  <div className="space-y-1.5">
                    <Label htmlFor="pod-cash">Cash received</Label>
                    <Input
                      id="pod-cash"
                      inputMode="decimal"
                      className="h-12 text-lg font-semibold"
                      value={cashReceived}
                      placeholder={String(task.codAmount)}
                      aria-describedby="pod-cash-check"
                      onChange={(e) => setCashReceived(e.target.value)}
                    />
                    <p
                      id="pod-cash-check"
                      className={cn(
                        "text-xs font-medium",
                        match?.kind === "match" && "text-status-delivered",
                        match?.kind === "short" && "text-destructive",
                        match?.kind === "over" && "text-status-ndr",
                        !match && "text-muted-foreground",
                      )}
                    >
                      {match === null
                        ? "Enter the cash you received"
                        : match.kind === "match"
                          ? "Matches"
                          : `${match.kind === "short" ? "Short by" : "Over by"} ${formatRs(match.difference)}`}
                    </p>
                  </div>
                </section>
              )}

              <section aria-label="Delivery OTP" className={SECTION}>
                <StepLabel>{`${otpStep} · Delivery OTP`}</StepLabel>
                <div className="flex items-center gap-2">
                  <Checkbox id="pod-otp" checked={otpVerified} onCheckedChange={(v) => setOtpVerified(v === true)} />
                  <Label htmlFor="pod-otp" className="font-normal">Customer confirmed with OTP</Label>
                </div>
                <p className="text-xs text-muted-foreground">Sending SMS codes needs backend support.</p>
              </section>

              <section aria-label="Proof" className={SECTION}>
                <StepLabel>{`${otpStep + 1} · Proof`}</StepLabel>
                <div className="space-y-1.5">
                  <Label htmlFor="pod-photo">Parcel photo</Label>
                  <Input
                    id="pod-photo"
                    type="file"
                    accept="image/jpeg,image/png"
                    capture="environment"
                    aria-describedby={photoError ? "pod-photo-error" : undefined}
                    aria-invalid={photoError ? true : undefined}
                    onChange={(e) => {
                      const file = e.target.files?.[0] ?? null;
                      if (file && file.size > MAX_PHOTO_BYTES) {
                        setPhotoFile(null);
                        setPhotoError("Photo is too large. Choose a file under 10MB.");
                        e.target.value = "";
                        return;
                      }
                      setPhotoError(null);
                      setPhotoFile(file);
                    }}
                  />
                  {photoError && <FieldError id="pod-photo-error">{photoError}</FieldError>}
                </div>
                <div className="space-y-1.5">
                  <Label>Recipient signature</Label>
                  <SignaturePad onChange={setSignatureBlob} />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="pod-received-by">Received by</Label>
                  <Input
                    id="pod-received-by"
                    className="h-11"
                    value={recipientName}
                    placeholder={task.recipientName}
                    onChange={(e) => setRecipientName(e.target.value)}
                  />
                </div>
              </section>
            </>
          ) : (
            <section aria-label="Failed attempt" className={SECTION}>
              <div className="space-y-1.5">
                <Label htmlFor="pod-reason">Reason</Label>
                <Select value={reason} onValueChange={(v) => setReason(v as RecordFailedAttemptFields["reason"])}>
                  <SelectTrigger id="pod-reason" aria-label="Reason" className="h-11">
                    <SelectValue placeholder="Choose a reason" />
                  </SelectTrigger>
                  <SelectContent>
                    {FAILURE_REASONS.map((r) => (
                      <SelectItem key={r} value={r}>{failureReasonLabel(r)}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="pod-notes">Notes</Label>
                <Textarea id="pod-notes" maxLength={500} value={notes} onChange={(e) => setNotes(e.target.value)} />
              </div>
              <div className="flex items-center gap-2">
                <Checkbox id="pod-contacted" checked={customerContacted} onCheckedChange={(v) => setCustomerContacted(v === true)} />
                <Label htmlFor="pod-contacted" className="font-normal">Customer contacted</Label>
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="pod-next-action">Next action</Label>
                <Input
                  id="pod-next-action"
                  className="h-11"
                  maxLength={500}
                  value={nextAction}
                  placeholder="e.g. Reattempt tomorrow after 4 PM"
                  onChange={(e) => setNextAction(e.target.value)}
                />
              </div>
            </section>
          )}
        </div>

        <div className="space-y-2 border-t border-border bg-card p-4">
          {serverError && <FieldError id="pod-server-error">{serverError}</FieldError>}
          {mode === "deliver" ? (
            <>
              <Button
                type="button"
                size="lg"
                className="h-14 w-full text-base"
                disabled={!canConfirm || completeDelivery.isPending}
                onClick={confirmDelivered}
              >
                {completeDelivery.isPending ? (
                  <Spinner className="h-4 w-4" />
                ) : codDue && match ? (
                  `Confirm delivered · ${formatRs(enteredCash ?? 0)} collected`
                ) : (
                  "Confirm delivered"
                )}
              </Button>
              <Button type="button" variant="outline" size="lg" className="h-12 w-full text-destructive" onClick={() => setMode("fail")}>
                Couldn&apos;t deliver
              </Button>
            </>
          ) : (
            <>
              <Button
                type="button"
                size="lg"
                className="h-14 w-full text-base"
                disabled={!reason || recordFailedAttempt.isPending}
                onClick={recordFailure}
              >
                {recordFailedAttempt.isPending ? <Spinner className="h-4 w-4" /> : "Record failed attempt"}
              </Button>
              <Button type="button" variant="outline" size="lg" className="h-12 w-full" onClick={() => setMode("deliver")}>
                Back to delivery
              </Button>
            </>
          )}
        </div>
      </div>
    </TooltipProvider>
  );
}
