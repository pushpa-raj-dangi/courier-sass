"use client";

import { useState } from "react";
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

// Plain button-row tab switcher, not @radix-ui/react-tabs — no Tabs primitive exists in this repo
// and a 6-way conditional render doesn't need one (CLAUDE.md's YAGNI guidance). Tasks 10-14 each
// append one more entry to TABS, one more `activeTab === "..."` branch, and their own Sheet(s) at
// the bottom — this file grows additively, never restructured, across the rest of this plan.
const TABS = [
  { key: "collections", label: "Collections" },
  { key: "rider-settlements", label: "Rider Settlements" },
  { key: "branch-settlements", label: "Branch Settlements" },
  { key: "merchant-settlements", label: "Merchant Settlements" },
  { key: "adjustments", label: "Adjustments" },
] as const;

type TabKey = (typeof TABS)[number]["key"];

export default function CodPage() {
  const [activeTab, setActiveTab] = useState<TabKey>("collections");

  return (
    <div className="space-y-4">
      <h1 className="font-display text-xl font-semibold">COD</h1>
      <div className="flex gap-1 border-b border-border" role="tablist">
        {TABS.map((tab) => (
          <button
            key={tab.key}
            type="button"
            role="tab"
            aria-selected={activeTab === tab.key}
            onClick={() => setActiveTab(tab.key)}
            className={`px-3 py-2 text-sm font-medium transition-colors ${
              activeTab === tab.key
                ? "border-b-2 border-foreground text-foreground"
                : "text-muted-foreground hover:text-foreground"
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {activeTab === "collections" && <CollectionsTab />}
      {activeTab === "rider-settlements" && <RiderSettlementsTab />}
      {activeTab === "branch-settlements" && <BranchSettlementsTab />}
      {activeTab === "merchant-settlements" && <MerchantSettlementsTab />}
      {activeTab === "adjustments" && <AdjustmentsTab />}

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
