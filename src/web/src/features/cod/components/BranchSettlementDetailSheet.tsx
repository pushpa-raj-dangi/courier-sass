"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { FieldError } from "@/components/ui/field-error";
import { ErrorState } from "@/components/states/ErrorState";
import { useBranchSettlements } from "@/features/cod/hooks/useBranchSettlements";
import { useRiderSettlements } from "@/features/cod/hooks/useRiderSettlements";
import { useCreateFinanceVerification } from "@/features/cod/hooks/useCreateFinanceVerification";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { codErrorMessage } from "@/features/cod/lib/codErrorMessage";

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

export function BranchSettlementDetailSheet() {
  const panel = useCodPanelStore((s) => s.panel);
  const close = useCodPanelStore((s) => s.close);
  const isOpen = panel.mode === "branch-settlement-detail";
  const id = panel.mode === "branch-settlement-detail" ? panel.id : "";

  const [verifyAmount, setVerifyAmount] = useState("");
  const [serverError, setServerError] = useState<string | null>(null);
  const canVerify = usePermission("cod.settle");

  const { data, isLoading, isError, refetch } = useBranchSettlements({ pageSize: 200 });
  const settlement = data?.items.find((b) => b.id === id);
  const { data: unverified } = useBranchSettlements({ unverifiedOnly: true, pageSize: 200 });
  const isVerified = settlement !== undefined && !unverified?.items.some((b) => b.id === id);
  const { data: riderSettlements } = useRiderSettlements({ pageSize: 200 });
  const { data: riders } = useRiders();
  const { data: branches } = useBranches();
  const branch = branches?.find((b) => b.id === settlement?.branchId);
  const ridersById = new Map((riders ?? []).map((r) => [r.id, r.licenseNumber]));
  const riderSettlementsById = new Map((riderSettlements?.items ?? []).map((r) => [r.id, r]));

  const verifyFinance = useCreateFinanceVerification();

  async function handleVerify() {
    setServerError(null);
    const amount = Number(verifyAmount);
    if (!Number.isFinite(amount) || amount <= 0 || !settlement) {
      setServerError("Amount must be greater than zero.");
      return;
    }
    try {
      await verifyFinance.mutateAsync({ branchCashSettlementId: settlement.id, amount });
      setVerifyAmount("");
    } catch (error) {
      setServerError(codErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) { setVerifyAmount(""); setServerError(null); close(); } }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>Branch Settlement</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isError && <ErrorState title="Couldn't load this settlement" description="Something went wrong." onRetry={() => refetch()} />}
          {settlement && (
            <>
              <div className="grid grid-cols-2 gap-4">
                <Field label="Branch" value={branch?.name ?? settlement.branchId} />
                <Field label="Confirmed Amount" value={`Rs. ${settlement.confirmedAmount.toFixed(2)}`} />
                <Field label="Reconciled At" value={new Date(settlement.reconciledAt).toLocaleString()} />
                <Field label="Verified" value={isVerified ? "Yes" : "Not yet"} />
              </div>
              <div className="space-y-2">
                <Label>Covered Rider Settlements</Label>
                <ul className="space-y-1 rounded-md border border-border p-3 text-sm">
                  {settlement.riderCashSettlementIds.map((rsId) => {
                    const riderSettlement = riderSettlementsById.get(rsId);
                    return (
                      <li key={rsId} className="text-foreground">
                        {riderSettlement ? `${ridersById.get(riderSettlement.riderId) ?? riderSettlement.riderId} — Rs. ${riderSettlement.declaredAmount.toFixed(2)}` : rsId}
                      </li>
                    );
                  })}
                </ul>
              </div>
              {!isVerified && canVerify && (
                <div className="space-y-2 rounded-md border border-border p-3">
                  <Label htmlFor="verify-amount">Finance Verify</Label>
                  <Input
                    id="verify-amount"
                    aria-label="Verification Amount"
                    type="number"
                    step="0.01"
                    value={verifyAmount}
                    onChange={(e) => setVerifyAmount(e.target.value)}
                    placeholder="Verified amount"
                  />
                  {serverError && <FieldError id="branch-settlement-detail-server-error">{serverError}</FieldError>}
                  <Button type="button" disabled={verifyFinance.isPending} onClick={handleVerify}>
                    {verifyFinance.isPending ? <Spinner className="h-4 w-4" /> : "Verify"}
                  </Button>
                </div>
              )}
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
