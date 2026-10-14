"use client";

import Link from "next/link";
import { ReceiptText } from "lucide-react";
import { SectionCard } from "@/components/page/SectionCard";
import { Skeleton } from "@/components/ui/skeleton";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { codSegments } from "@/features/reports/lib/dashboardModel";
import { useCodFlow } from "@/features/cod/hooks/useCodFlow";
import { useRiderCash } from "@/features/cod/hooks/useRiderCash";
import { formatRs } from "@/features/cod/lib/codFormat";
import { stageAmount } from "@/features/cod/lib/flowModel";
import type { CodFinancialSummary } from "@/features/reports/types";


export function CodRangeCard({ financial, rangeLabel, range }: { financial: CodFinancialSummary; rangeLabel: string; range: { from?: string; to?: string } }) {
  const canCod = usePermission("cod.read");
  const flow = useCodFlow(range, { enabled: canCod });
  const cash = useRiderCash({}, { enabled: canCod });
  const riderHeld = cash.data ? cash.data.items.reduce((t, h) => t + h.amount, 0) : undefined;
  const deposited = stageAmount(flow.data, "riderDeposited");
  const verified = stageAmount(flow.data, "financeVerified");
  const unverified = deposited !== undefined && verified !== undefined ? deposited - verified : undefined;
  const segments = codSegments(financial);
  const total = segments.reduce((t, s) => t + s.value, 0);
  return (
    <SectionCard title="COD" description={rangeLabel} action={<span className="text-xs text-muted-foreground">Expected <b className="text-foreground">{formatRs(financial.expected)}</b></span>}>
      <div className="flex h-2.5 gap-0.5 overflow-hidden rounded-full bg-muted" aria-hidden="true">
        {total > 0 && segments.map((s) => (
          <span key={s.key} style={{ width: `${(s.value / total) * 100}%`, backgroundColor: `hsl(var(--${s.token}))` }} />
        ))}
      </div>
      <dl className="mt-3 space-y-1.5">
        {segments.map((s) => (
          <div key={s.key} className="flex items-center justify-between text-[13px]">
            <dt className="flex items-center gap-2 text-muted-foreground">
              <span className="h-2 w-2 rounded-full" style={{ backgroundColor: `hsl(var(--${s.token}))` }} aria-hidden="true" />
              {s.label}
            </dt>
            <dd className="font-medium tabular-nums">{formatRs(s.value)}</dd>
          </div>
        ))}
      </dl>
      <div className="mt-4 flex items-center gap-3 rounded-lg bg-surface-sunken p-3">
        <ReceiptText className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
        <span className="flex flex-1 flex-col">
          <span className="text-xs text-muted-foreground">Merchant payable</span>
          <span className="text-[15px] font-semibold tabular-nums">{formatRs(financial.merchantPayable)}</span>
        </span>
        {canCod && (
          <Link href="/cod" className="text-[13px] font-medium text-primary hover:underline">
            Review
          </Link>
        )}
      </div>
      {canCod && (
        <dl aria-label="Cash in hand" className="mt-3 grid grid-cols-2 gap-3 rounded-lg border border-border p-3">
          {[
            { label: "Rider-held (now)", value: riderHeld, loading: cash.isLoading, error: cash.isError },
            { label: "Deposited, not yet verified", value: unverified, loading: flow.isLoading, error: flow.isError },
          ].map((cell) => (
            <div key={cell.label} className="flex flex-col gap-1">
              <dt className="text-xs text-muted-foreground">{cell.label}</dt>
              <dd className="text-base font-semibold tabular-nums">
                {cell.loading ? <Skeleton className="h-5 w-20" /> : cell.error || cell.value === undefined ? "—" : formatRs(cell.value)}
              </dd>
            </div>
          ))}
        </dl>
      )}
    </SectionCard>
  );
}
