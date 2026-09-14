"use client";

import { useState } from "react";
import { Banknote, Wallet, Building2, HandCoins, Receipt, PackageCheck } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { DatePicker } from "@/components/ui/date-picker";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { useCodReconciliation } from "@/features/cod/hooks/useCodReconciliation";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";

function formatAmount(value: number): string {
  return `Rs. ${value.toFixed(2)}`;
}

export function ReconciliationTab() {
  const [branchId, setBranchId] = useState<string | "all">("all");
  const [merchantId, setMerchantId] = useState<string | "all">("all");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  const { data: branches } = useBranches();
  const { data: merchants } = useMerchants();
  const { data: summary, isLoading } = useCodReconciliation({
    branchId: branchId === "all" ? undefined : branchId,
    merchantId: merchantId === "all" ? undefined : merchantId,
    from: from || undefined,
    to: to || undefined,
  });

  // Tile tokens deliberately match the Cod.* STATUS_CONFIG mapping for the corresponding lifecycle
  // stage (statusConfig.ts) — including its deliberate avoidance of --status-in-transit for
  // Rider-Deposited, since these six tiles sit adjacent in one dashboard just like a status badge
  // list, and would reproduce the same dark-mode duplicate otherwise (see CLARIFICATIONS.md #17).
  const tiles = summary
    ? [
        { label: "Expected COD", value: formatAmount(summary.expectedCod), icon: Wallet, badgeClass: "bg-status-created/15 text-status-created" },
        { label: "Collected COD", value: formatAmount(summary.collectedCod), icon: Banknote, badgeClass: "bg-status-out-for-delivery/15 text-status-out-for-delivery" },
        { label: "Rider-Deposited COD", value: formatAmount(summary.riderDepositedCod), icon: PackageCheck, badgeClass: "bg-status-sealed/15 text-status-sealed" },
        { label: "Branch-Received COD", value: formatAmount(summary.branchReceivedCod), icon: Building2, badgeClass: "bg-status-received/15 text-status-received" },
        { label: "Merchant-Payable COD", value: formatAmount(summary.merchantPayableCod), icon: HandCoins, badgeClass: "bg-status-ndr/15 text-status-ndr" },
        { label: "Total Courier Fees", value: formatAmount(summary.totalCourierFees), icon: Receipt, badgeClass: "bg-muted text-muted-foreground" },
      ]
    : [];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <Label htmlFor="recon-branch">Branch</Label>
          <Select value={branchId} onValueChange={setBranchId}>
            <SelectTrigger id="recon-branch" className="w-48" aria-label="Filter by branch">
              <SelectValue placeholder="All branches" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All branches</SelectItem>
              {branches?.map((b) => (
                <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="recon-merchant">Merchant</Label>
          <Select value={merchantId} onValueChange={setMerchantId}>
            <SelectTrigger id="recon-merchant" className="w-48" aria-label="Filter by merchant">
              <SelectValue placeholder="All merchants" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All merchants</SelectItem>
              {merchants?.map((m) => (
                <SelectItem key={m.id} value={m.id}>{m.displayName}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="recon-from">From</Label>
          <DatePicker id="recon-from" aria-label="From date" value={from} onChange={setFrom} clearable />
        </div>
        <div className="space-y-1">
          <Label htmlFor="recon-to">To</Label>
          <DatePicker id="recon-to" aria-label="To date" value={to} onChange={setTo} clearable />
        </div>
      </div>

      {isLoading && (
        <Card>
          <CardHeader>
            <CardTitle className="font-display text-base">COD Reconciliation</CardTitle>
          </CardHeader>
          <CardContent className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3" role="status" aria-live="polite">
            <span className="sr-only">Loading</span>
            {Array.from({ length: 6 }).map((_, i) => (
              <Skeleton key={i} className="h-16 w-full rounded-lg" />
            ))}
          </CardContent>
        </Card>
      )}

      {summary && (
        <Card>
          <CardHeader>
            <CardTitle className="font-display text-base">COD Reconciliation</CardTitle>
          </CardHeader>
          <CardContent className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {tiles.map((tile) => {
              const Icon = tile.icon;
              return (
                <div key={tile.label} className="flex items-center gap-3 rounded-lg border border-border p-4">
                  <span className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-lg ${tile.badgeClass}`}>
                    <Icon className="h-5 w-5" strokeWidth={2} />
                  </span>
                  <div className="flex flex-col gap-0.5">
                    <span className="text-xs text-muted-foreground">{tile.label}</span>
                    <span className="font-sans text-lg font-semibold tabular-nums text-foreground">{tile.value}</span>
                  </div>
                </div>
              );
            })}
          </CardContent>
        </Card>
      )}
    </div>
  );
}
