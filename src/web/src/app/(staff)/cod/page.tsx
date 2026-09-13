"use client";

import { useState } from "react";
import { Wallet, CheckCircle2, Clock, Landmark, Building2 } from "lucide-react";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { SummaryStatsBar, type StatTile } from "@/components/summary-stats/SummaryStatsBar";
import { useCodSummary } from "@/features/cod/hooks/useCodSummary";
import type { CodFinancialSummary } from "@/features/reports/types";
import { CollectionsTab } from "@/features/cod/components/CollectionsTab";
import { CollectionDetailSheet } from "@/features/cod/components/CollectionDetailSheet";
import { RiderSettlementsTab } from "@/features/cod/components/RiderSettlementsTab";
import { RiderSettlementCreateSheet } from "@/features/cod/components/RiderSettlementCreateSheet";
import { RiderSettlementDetailSheet } from "@/features/cod/components/RiderSettlementDetailSheet";
import { BranchSettlementsTab } from "@/features/cod/components/BranchSettlementsTab";
import { BranchSettlementCreateSheet } from "@/features/cod/components/BranchSettlementCreateSheet";
import { BranchSettlementDetailSheet } from "@/features/cod/components/BranchSettlementDetailSheet";
import { MerchantSettlementsTab } from "@/features/cod/components/MerchantSettlementsTab";
import { MerchantSettlementCreateSheet } from "@/features/cod/components/MerchantSettlementCreateSheet";
import { MerchantSettlementDetailSheet } from "@/features/cod/components/MerchantSettlementDetailSheet";
import { AdjustmentsTab } from "@/features/cod/components/AdjustmentsTab";
import { AdjustmentDetailSheet } from "@/features/cod/components/AdjustmentDetailSheet";
import { ReconciliationTab } from "@/features/cod/components/ReconciliationTab";

const currencyFormatter = new Intl.NumberFormat("en-NP", {
  style: "currency",
  currency: "NPR",
  notation: "compact",
  maximumFractionDigits: 1,
});

function buildTiles(summary: CodFinancialSummary): StatTile[] {
  return [
    { label: "Expected", value: currencyFormatter.format(summary.expected), icon: Wallet, badgeClass: "bg-muted text-muted-foreground" },
    { label: "Collected", value: currencyFormatter.format(summary.collected), icon: CheckCircle2, badgeClass: "bg-status-delivered/15 text-status-delivered" },
    { label: "Pending", value: currencyFormatter.format(summary.pending), icon: Clock, badgeClass: "bg-accent text-primary", highlight: true },
    { label: "Settled", value: currencyFormatter.format(summary.settled), icon: Landmark, badgeClass: "bg-status-received/15 text-status-received" },
    { label: "Merchant Payable", value: currencyFormatter.format(summary.merchantPayable), icon: Building2, badgeClass: "bg-status-sealed/15 text-status-sealed" },
  ];
}

// This file grows additively — new tabs, their own `TabsContent`, and their own Sheet(s) appended
// at the bottom, never restructured.
const TABS = [
  { key: "collections", label: "Collections" },
  { key: "rider-settlements", label: "Rider Settlements" },
  { key: "branch-settlements", label: "Branch Settlements" },
  { key: "merchant-settlements", label: "Merchant Settlements" },
  { key: "adjustments", label: "Adjustments" },
  { key: "reconciliation", label: "Reconciliation" },
] as const;

type TabKey = (typeof TABS)[number]["key"];

export default function CodPage() {
  const [activeTab, setActiveTab] = useState<TabKey>("collections");
  const summary = useCodSummary();

  return (
    <div className="space-y-4">
      <h1 className="font-display text-xl font-semibold">COD</h1>
      <SummaryStatsBar
        tiles={summary.data ? buildTiles(summary.data) : []}
        isLoading={summary.isLoading}
        isError={summary.isError}
        skeletonCount={5}
      />
      <Tabs value={activeTab} onValueChange={(v) => setActiveTab(v as TabKey)}>
        <TabsList>
          {TABS.map((tab) => (
            <TabsTrigger key={tab.key} value={tab.key}>{tab.label}</TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="collections"><CollectionsTab /></TabsContent>
        <TabsContent value="rider-settlements"><RiderSettlementsTab /></TabsContent>
        <TabsContent value="branch-settlements"><BranchSettlementsTab /></TabsContent>
        <TabsContent value="merchant-settlements"><MerchantSettlementsTab /></TabsContent>
        <TabsContent value="adjustments"><AdjustmentsTab /></TabsContent>
        <TabsContent value="reconciliation"><ReconciliationTab /></TabsContent>
      </Tabs>

      <CollectionDetailSheet />
      <RiderSettlementCreateSheet />
      <RiderSettlementDetailSheet />
      <BranchSettlementCreateSheet />
      <BranchSettlementDetailSheet />
      <MerchantSettlementCreateSheet />
      <MerchantSettlementDetailSheet />
      <AdjustmentDetailSheet />
    </div>
  );
}
