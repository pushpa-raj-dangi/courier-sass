"use client";

import Link from "next/link";
import { ReceiptText } from "lucide-react";
import { SectionCard } from "@/components/page/SectionCard";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { codSegments } from "@/features/reports/lib/dashboardModel";
import type { CodFinancialSummary } from "@/features/reports/types";

const npr = new Intl.NumberFormat("en-NP", { style: "currency", currency: "NPR", maximumFractionDigits: 0 });

export function CodRangeCard({ financial, rangeLabel }: { financial: CodFinancialSummary; rangeLabel: string }) {
  const canCod = usePermission("cod.read");
  const segments = codSegments(financial);
  const total = segments.reduce((t, s) => t + s.value, 0);
  return (
    <SectionCard title="COD" description={rangeLabel} action={<span className="text-xs text-muted-foreground">Expected <b className="text-foreground">{npr.format(financial.expected)}</b></span>}>
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
            <dd className="font-medium tabular-nums">{npr.format(s.value)}</dd>
          </div>
        ))}
      </dl>
      <div className="mt-4 flex items-center gap-3 rounded-lg bg-surface-sunken p-3">
        <ReceiptText className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
        <span className="flex flex-1 flex-col">
          <span className="text-xs text-muted-foreground">Merchant payable</span>
          <span className="text-[15px] font-semibold tabular-nums">{npr.format(financial.merchantPayable)}</span>
        </span>
        {canCod && (
          <Link href="/cod" className="text-[13px] font-medium text-primary hover:underline">
            Review
          </Link>
        )}
      </div>
      <ComingSoonSlot className="mt-3 p-4" title="Rider-held vs deposited" description="How much collected cash riders still hold versus deposited but unverified." />
    </SectionCard>
  );
}
