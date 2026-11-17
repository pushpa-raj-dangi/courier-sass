"use client";

import Link from "next/link";
import { AlertTriangle } from "lucide-react";
import { SectionCard } from "@/components/page/SectionCard";
import { useMerchantShipmentCounts } from "@/features/merchant-shipments/hooks/useMerchantShipmentCounts";
import { attentionRows } from "../lib/dashboardLogic";

export function NeedsAttention() {
  const counts = useMerchantShipmentCounts();
  const loading = counts.needsAttention.isLoading || counts.returning.isLoading;
  const rows = attentionRows(counts.needsAttention.count, counts.returning.count);

  return (
    <SectionCard title="Needs your attention" icon={AlertTriangle} className="border-destructive/30 bg-destructive/5">
      {loading ? (
        <p className="text-sm text-muted-foreground">Checking…</p>
      ) : rows.length === 0 ? (
        <p className="text-sm text-muted-foreground">Nothing needs you right now</p>
      ) : (
        <ul className="space-y-2">
          {rows.map((row) => (
            <li key={row.key}>
              <Link
                href={`/merchant/shipments?tab=${row.key}`}
                className="text-sm font-medium text-destructive hover:underline"
              >
                {row.text}
              </Link>
            </li>
          ))}
        </ul>
      )}
    </SectionCard>
  );
}
