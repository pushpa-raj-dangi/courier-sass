"use client";

import { Suspense } from "react";
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";
import { PageHeader } from "@/components/page/PageHeader";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";
import { useSession } from "@/features/auth/hooks/useSession";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useCodFlow } from "@/features/cod/hooks/useCodFlow";
import { useCodUrlState } from "@/features/cod/hooks/useCodUrlState";
import { COD_TABS, RANGE_LABELS, RANGE_PRESETS, parseCodTab, parseRangePreset } from "@/features/cod/lib/codTabs";
import { presetDays, toUtcRange } from "@/lib/dates/rangePresets";
import { CodFlowStrip } from "@/features/cod/components/CodFlowStrip";
import { CollectionsTab } from "@/features/cod/components/CollectionsTab";
import { CollectionDetailSheet } from "@/features/cod/components/CollectionDetailSheet";
import { RiderDepositsTab } from "@/features/cod/components/RiderDepositsTab";
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

const ALL_BRANCHES = "all";

function NeedsBackendButton({ label }: { label: string }) {
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span tabIndex={0}>
          <Button type="button" variant="outline" disabled>
            {label}
          </Button>
        </span>
      </TooltipTrigger>
      <TooltipContent>Needs backend</TooltipContent>
    </Tooltip>
  );
}

function CodPageContent() {
  const { tab, range, branchId, setTab, setRange, setBranch } = useCodUrlState();
  const days = presetDays(range);
  const { from, to } = toUtcRange(days.from, days.to);
  const flow = useCodFlow({ branchId, from, to });

  const { data: session } = useSession();
  const { data: branches } = useBranches();
  const scopedBranches = (branches ?? []).filter(
    (b) => session?.hasAllBranchesScope || session?.branches.some((s) => s.branchId === b.id),
  );
  const showBranchPicker = Boolean(session?.hasAllBranchesScope) || scopedBranches.length > 1;
  const branchLabel = branchId ? scopedBranches.find((b) => b.id === branchId)?.name ?? "Selected branch" : "All my branches";

  return (
    <TooltipProvider>
      <div className="space-y-5">
        <PageHeader
          eyebrow="Finance"
          title="COD reconciliation"
          description={`${branchLabel} · ${RANGE_LABELS[range]}`}
          actions={
            <div className="flex flex-wrap items-center gap-2">
              {showBranchPicker && (
                <Select value={branchId ?? ALL_BRANCHES} onValueChange={(v) => setBranch(v === ALL_BRANCHES ? undefined : v)}>
                  <SelectTrigger className="w-48" aria-label="Branch">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={ALL_BRANCHES}>All my branches</SelectItem>
                    {scopedBranches.map((b) => (
                      <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
              <Select value={range} onValueChange={(v) => setRange(parseRangePreset(v))}>
                <SelectTrigger className="w-36" aria-label="Date range">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {RANGE_PRESETS.map((p) => (
                    <SelectItem key={p} value={p}>{RANGE_LABELS[p]}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <NeedsBackendButton label="Download ledger" />
              <NeedsBackendButton label="Close branch day" />
            </div>
          }
        />

        <CodFlowStrip flow={flow.data} isLoading={flow.isLoading} isError={flow.isError} onRetry={() => flow.refetch()} onSelectTab={setTab} />

        <Tabs value={tab} onValueChange={(v) => setTab(parseCodTab(v))}>
          <TabsList>
            {COD_TABS.map((t) => (
              <TabsTrigger key={t.key} value={t.key}>{t.label}</TabsTrigger>
            ))}
          </TabsList>
          <TabsContent value="collections"><CollectionsTab branchId={branchId} from={from} to={to} /></TabsContent>
          <TabsContent value="rider-deposits"><RiderDepositsTab branchId={branchId} from={from} to={to} /></TabsContent>
          <TabsContent value="branch-settlements"><BranchSettlementsTab branchId={branchId} from={from} to={to} /></TabsContent>
          <TabsContent value="merchant-settlements"><MerchantSettlementsTab branchFiltered={Boolean(branchId)} from={from} to={to} /></TabsContent>
          <TabsContent value="adjustments"><AdjustmentsTab branchFiltered={Boolean(branchId)} from={from} to={to} /></TabsContent>
          <TabsContent value="ledger">
            <ComingSoonSlot title="Ledger" description="Every COD money movement with before/after values and export." minHeight={220} />
          </TabsContent>
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
    </TooltipProvider>
  );
}

export default function CodPage() {
  return (
    <Suspense>
      <CodPageContent />
    </Suspense>
  );
}
