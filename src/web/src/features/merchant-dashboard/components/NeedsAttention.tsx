"use client";

import Link from "next/link";
import { useQueryClient } from "@tanstack/react-query";
import { AlertTriangle } from "lucide-react";
import { SectionCard } from "@/components/page/SectionCard";
import { Skeleton } from "@/components/ui/skeleton";
import { useMerchantShipmentCounts } from "@/features/merchant-shipments/hooks/useMerchantShipmentCounts";
import { attentionRows } from "../lib/dashboardLogic";
import { SectionError } from "./SectionError";

export function NeedsAttention() {
  const queryClient = useQueryClient();
  const { needsAttention, returning } = useMerchantShipmentCounts();
  const loading = needsAttention.isLoading || returning.isLoading;

  let body;
  if (loading) {
    body = <Skeleton data-testid="dashboard-skeleton" className="h-12 w-full" />;
  } else if (needsAttention.count === null || returning.count === null) {
    body = (
      <SectionError
        message="Couldn't load these counts."
        onRetry={() => queryClient.invalidateQueries({ queryKey: ["merchant-shipments", "count"] })}
      />
    );
  } else {
    const rows = attentionRows(needsAttention.count, returning.count);
    body =
      rows.length === 0 ? (
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
      );
  }

  return (
    <SectionCard title="Needs your attention" icon={AlertTriangle} className="border-destructive/30 bg-destructive/5">
      {body}
    </SectionCard>
  );
}
