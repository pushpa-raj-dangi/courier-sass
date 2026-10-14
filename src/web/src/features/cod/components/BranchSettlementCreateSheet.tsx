"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { useRiderSettlements } from "@/features/cod/hooks/useRiderSettlements";
import { useCreateBranchSettlement } from "@/features/cod/hooks/useCreateBranchSettlement";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { codErrorMessage } from "@/features/cod/lib/codErrorMessage";
import { formatRs } from "@/features/cod/lib/codFormat";

export function BranchSettlementCreateSheet() {
  const panel = useCodPanelStore((s) => s.panel);
  const close = useCodPanelStore((s) => s.close);
  const openDetail = useCodPanelStore((s) => s.openBranchSettlementDetail);
  const isOpen = panel.mode === "branch-settlement-create";

  const [branchId, setBranchId] = useState<string | undefined>(undefined);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [confirmedAmount, setConfirmedAmount] = useState("");
  const [validationError, setValidationError] = useState<string | null>(null);
  const [serverError, setServerError] = useState<string | null>(null);

  const { data: branches } = useBranches();
  const { data: riders } = useRiders();
  const { data: eligible } = useRiderSettlements({ branchId, unreconciledOnly: true, pageSize: 200 });
  const createSettlement = useCreateBranchSettlement();

  const ridersById = new Map((riders ?? []).map((r) => [r.id, r.licenseNumber]));

  function reset() {
    setBranchId(undefined);
    setSelectedIds(new Set());
    setConfirmedAmount("");
    setValidationError(null);
    setServerError(null);
  }

  function toggle(id: string) {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  }

  async function handleSubmit() {
    setValidationError(null);
    setServerError(null);
    const amount = Number(confirmedAmount);
    if (!branchId) {
      setValidationError("Branch is required.");
      return;
    }
    if (selectedIds.size === 0) {
      setValidationError("Select at least one rider settlement.");
      return;
    }
    if (!Number.isFinite(amount) || amount <= 0) {
      setValidationError("Confirmed amount must be greater than zero.");
      return;
    }
    try {
      const created = await createSettlement.mutateAsync({
        branchId,
        riderCashSettlementIds: Array.from(selectedIds),
        confirmedAmount: amount,
      });
      reset();
      openDetail(created.id);
    } catch (error) {
      setServerError(codErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) { reset(); close(); } }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>New Branch Settlement</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-4">
          <div className="space-y-2">
            <Label htmlFor="branchId">Branch</Label>
            <Select value={branchId} onValueChange={(v) => { setBranchId(v); setSelectedIds(new Set()); }}>
              <SelectTrigger id="branchId" aria-label="Branch">
                <SelectValue placeholder="Select branch" />
              </SelectTrigger>
              <SelectContent>
                {branches?.map((b) => (
                  <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          {branchId && (
            <div className="space-y-2">
              <Label>Unreconciled Rider Settlements</Label>
              <div className="space-y-2 rounded-md border border-border p-3">
                {eligible?.items.length === 0 && (
                  <p className="text-sm text-muted-foreground">No unreconciled rider settlements for this branch.</p>
                )}
                {eligible?.items.map((r) => (
                  <div key={r.id} className="flex items-center gap-2">
                    <Checkbox
                      id={`rider-settlement-${r.id}`}
                      checked={selectedIds.has(r.id)}
                      onCheckedChange={() => toggle(r.id)}
                    />
                    <Label htmlFor={`rider-settlement-${r.id}`}>
                      {ridersById.get(r.riderId) ?? r.riderId} — {formatRs(r.declaredAmount)}
                    </Label>
                  </div>
                ))}
              </div>
            </div>
          )}
          <div className="space-y-2">
            <Label htmlFor="confirmedAmount">Confirmed Amount</Label>
            <Input
              id="confirmedAmount"
              aria-label="Confirmed Amount"
              type="number"
              step="0.01"
              value={confirmedAmount}
              onChange={(e) => setConfirmedAmount(e.target.value)}
            />
          </div>
          {validationError && <FieldError id="branch-settlement-validation-error">{validationError}</FieldError>}
          {serverError && <FieldError id="branch-settlement-server-error">{serverError}</FieldError>}
          <Button type="button" disabled={createSettlement.isPending} onClick={handleSubmit}>
            {createSettlement.isPending ? <Spinner className="h-4 w-4" /> : "Create Settlement"}
          </Button>
        </div>
      </SheetContent>
    </Sheet>
  );
}
