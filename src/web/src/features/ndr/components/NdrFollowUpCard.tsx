"use client";

import { useState } from "react";
import { Controller, useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { MessageSquare } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DateTimePicker } from "@/components/ui/date-time-picker";
import { FieldError } from "@/components/ui/field-error";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { Textarea } from "@/components/ui/textarea";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { SectionCard } from "@/components/page/SectionCard";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useSession } from "@/features/auth/hooks/useSession";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { useUpdateNdr } from "@/features/ndr/hooks/useUpdateNdr";
import { NEXT_NDR_STATUSES, ndrStatusLabel, ownerLabel } from "@/features/ndr/lib/ndrModel";
import { ndrFollowUpSchema, type NdrFollowUpFormValues } from "@/features/ndr/schema";
import type { Ndr, NdrStatus } from "@/features/ndr/types";

const followUpFormat = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });

function Recorded({ ndr }: { ndr: Ndr }) {
  const rows = [
    { label: "Customer response", value: ndr.customerResponse },
    { label: "Next action", value: ndr.nextAction },
    { label: "Follow up", value: ndr.followUpAt ? followUpFormat.format(new Date(ndr.followUpAt)) : null },
  ].filter((r) => r.value);
  if (rows.length === 0) return null;
  return (
    <dl className="mb-4 grid grid-cols-1 gap-2 rounded-lg bg-muted/50 p-3 sm:grid-cols-3">
      {rows.map((r) => (
        <div key={r.label} className="space-y-0.5">
          <dt className="text-2xs text-muted-foreground">{r.label}</dt>
          <dd className="text-sm">{r.value}</dd>
        </div>
      ))}
    </dl>
  );
}

// UpdateAsync accepts exactly one legal forward move per save, so the form exists only while a
// move is possible (Open, Contacting, CustomerConfirmed) and Status lists only those moves.
export function NdrFollowUpCard({ ndr, onSaved }: { ndr: Ndr; onSaved: () => void }) {
  const canManage = usePermission("ndr.manage");
  const { data: session } = useSession();
  const names = usePeopleNames();
  const updateNdr = useUpdateNdr(ndr.id);
  const [serverError, setServerError] = useState<string | null>(null);
  const moves = NEXT_NDR_STATUSES[ndr.status];
  const editable = canManage && moves.length > 0;

  const { register, control, handleSubmit, formState: { errors } } = useForm<NdrFollowUpFormValues>({
    resolver: zodResolver(ndrFollowUpSchema),
    defaultValues: { status: moves.length === 1 ? moves[0] : "", customerResponse: "", nextAction: "", followUpAt: "" },
  });

  async function onSubmit(values: NdrFollowUpFormValues) {
    setServerError(null);
    try {
      await updateNdr.mutateAsync({
        status: values.status as NdrStatus,
        customerResponse: values.customerResponse.trim() || undefined,
        nextAction: values.nextAction.trim() || undefined,
        followUpAt: values.followUpAt ? new Date(values.followUpAt).toISOString() : undefined,
      });
      onSaved();
    } catch {
      setServerError("Couldn't save this follow-up. Check the fields and try again.");
    }
  }

  const owner = ownerLabel(ndr.assignedStaffUserId, session?.userId, names.userName);
  const ownerAction = (
    <div className="flex items-center gap-2">
      <span className="text-xs text-muted-foreground">{owner === "You" ? "Owner: you" : `Owner: ${owner}`}</span>
      <Tooltip>
        <TooltipTrigger asChild>
          <span tabIndex={0}>
            <Button type="button" variant="outline" size="sm" disabled>Assign to…</Button>
          </span>
        </TooltipTrigger>
        <TooltipContent>Needs backend</TooltipContent>
      </Tooltip>
    </div>
  );

  return (
    <SectionCard title="Follow-up" icon={MessageSquare} action={ownerAction}>
      <Recorded ndr={ndr} />
      {!canManage && <p className="text-sm text-muted-foreground">You can view this follow-up but not change it.</p>}
      {canManage && !editable && <p className="text-sm text-muted-foreground">The customer outcome is recorded. Resolve the NDR below.</p>}
      {editable && (
        <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="ndr-customer-response">Customer response</Label>
            <Textarea id="ndr-customer-response" {...register("customerResponse")} />
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="ndr-next-status">Next status</Label>
              <Controller
                name="status"
                control={control}
                render={({ field }) => (
                  <Select value={field.value || undefined} onValueChange={field.onChange}>
                    <SelectTrigger
                      id="ndr-next-status"
                      aria-invalid={!!errors.status}
                      aria-describedby={errors.status ? "ndr-next-status-error" : undefined}
                    >
                      <SelectValue placeholder="Choose…" />
                    </SelectTrigger>
                    <SelectContent>
                      {moves.map((s) => (
                        <SelectItem key={s} value={s}>{ndrStatusLabel(s)}</SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
              {errors.status && <FieldError id="ndr-next-status-error">{errors.status.message}</FieldError>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="ndr-follow-up-at">Follow up again</Label>
              <Controller
                name="followUpAt"
                control={control}
                render={({ field }) => <DateTimePicker id="ndr-follow-up-at" value={field.value} onChange={field.onChange} clearable />}
              />
            </div>
          </div>
          <div className="space-y-2">
            <Label htmlFor="ndr-next-action">Next action note</Label>
            <Input id="ndr-next-action" {...register("nextAction")} />
          </div>
          {serverError && <FieldError id="ndr-follow-up-server-error">{serverError}</FieldError>}
          <Button type="submit" variant="outline" disabled={updateNdr.isPending}>
            {updateNdr.isPending ? <Spinner className="h-4 w-4" /> : "Save follow-up"}
          </Button>
        </form>
      )}
    </SectionCard>
  );
}
