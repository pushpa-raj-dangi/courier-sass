"use client";

import { ChevronRight } from "lucide-react";
import { SectionError } from "@/components/states/SectionError";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";
import { formatRs } from "@/features/cod/lib/codFormat";
import { plural, settlementsCaption } from "@/features/merchant-cod/lib/codCopy";
import { useMerchantCodSummary } from "@/features/merchant-cod/hooks/useMerchantCodSummary";

interface Step {
  key: string;
  label: string;
  /** null = unknown (summary failed) — rendered "—", never Rs 0. */
  amount: number | null;
  caption: string | null;
  highlighted?: boolean;
}

/** Where the merchant's COD money is: collected › in settlement › settled this month. */
export function CodMoneyStrip() {
  const { data, isLoading, isError, refetch } = useMerchantCodSummary();

  const steps: Step[] = [
    {
      key: "collected",
      label: "Collected, not yet settled",
      amount: data?.collectedNotSettled.amount ?? null,
      caption: data ? plural(data.collectedNotSettled.count, "parcel") : null,
      highlighted: true,
    },
    {
      key: "in-settlement",
      label: "In settlement",
      amount: data?.inSettlement.amount ?? null,
      caption: data ? settlementsCaption(data.inSettlement.count) : null,
    },
    {
      key: "settled",
      label: "Settled this month",
      amount: data?.settledThisMonth.amount ?? null,
      caption: data ? plural(data.settledThisMonth.payouts, "payout") : null,
    },
  ];

  return (
    <div className="space-y-3">
      <ol aria-label="COD money flow" className="grid gap-2.5 sm:grid-cols-[1fr_auto_1fr_auto_1fr] sm:items-stretch">
        {steps.flatMap((step, index) => {
          const tile = (
            <li
              key={step.key}
              aria-current={step.highlighted ? "step" : undefined}
              className={cn(
                "flex flex-col gap-1.5 rounded-lg border px-4 py-3",
                step.highlighted ? "border-primary bg-primary text-primary-foreground" : "border-border bg-card text-foreground"
              )}
            >
              <span className={cn("text-xs", step.highlighted ? "text-primary-foreground/80" : "text-muted-foreground")}>{step.label}</span>
              {isLoading ? (
                <Skeleton data-testid="cod-strip-skeleton" className="h-8 w-28" />
              ) : (
                <span className="text-2xl font-semibold tabular-nums tracking-tight">{step.amount === null ? "—" : formatRs(step.amount)}</span>
              )}
              {!isLoading && step.caption && (
                <span className={cn("text-2xs", step.highlighted ? "text-primary-foreground/80" : "text-muted-foreground")}>{step.caption}</span>
              )}
            </li>
          );
          if (index === steps.length - 1) return [tile];
          return [
            tile,
            <li key={`${step.key}-arrow`} aria-hidden="true" className="hidden items-center text-muted-foreground sm:flex">
              <ChevronRight className="h-5 w-5" strokeWidth={2} />
            </li>,
          ];
        })}
      </ol>
      {isError && <SectionError message="Couldn't load your COD summary." onRetry={() => refetch()} />}
    </div>
  );
}
