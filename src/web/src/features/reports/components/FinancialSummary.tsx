import { FileText, Download, Clock, CheckCircle2, Wallet } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import type { CodFinancialSummary } from "@/features/reports/types";

const currencyFormatter = new Intl.NumberFormat("en-NP", {
  style: "currency",
  currency: "NPR",
  notation: "compact",
  maximumFractionDigits: 1,
});

const exactCurrencyFormatter = new Intl.NumberFormat("en-NP", {
  style: "currency",
  currency: "NPR",
});

const FIELDS: Array<{ key: keyof CodFinancialSummary; label: string; icon: typeof FileText }> = [
  { key: "expected", label: "Expected", icon: FileText },
  { key: "collected", label: "Collected", icon: Download },
  { key: "pending", label: "Pending", icon: Clock },
  { key: "settled", label: "Settled", icon: CheckCircle2 },
];

export function FinancialSummary({ data }: { data: CodFinancialSummary }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-3 font-display text-base">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-muted">
            <Wallet className="h-4 w-4 text-muted-foreground" strokeWidth={2} />
          </span>
          COD Summary
        </CardTitle>
        <CardDescription>Cash-on-delivery collection status</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-6 lg:flex-row lg:items-stretch">
        <div className="grid flex-1 grid-cols-2 gap-6 sm:grid-cols-4">
          {FIELDS.map((field) => {
            const Icon = field.icon;
            return (
              <div key={field.key} className="flex flex-col gap-2">
                <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-muted">
                  <Icon className="h-4 w-4 text-muted-foreground" strokeWidth={2} />
                </span>
                <span className="text-xs text-muted-foreground">{field.label}</span>
                <span
                  className="font-sans text-lg font-semibold tabular-nums text-foreground"
                  title={exactCurrencyFormatter.format(data[field.key])}
                >
                  {currencyFormatter.format(data[field.key])}
                </span>
              </div>
            );
          })}
        </div>
        <div className="flex items-center gap-3 rounded-lg bg-accent p-4 lg:w-64">
          <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-status-in-transit/15">
            <Wallet className="h-5 w-5 text-status-in-transit" strokeWidth={2} />
          </span>
          <div className="flex flex-col gap-1">
            <span className="text-xs text-accent-foreground/70">Merchant Payable</span>
            <span
              className="font-sans text-xl font-semibold tabular-nums text-status-in-transit"
              title={exactCurrencyFormatter.format(data.merchantPayable)}
            >
              {currencyFormatter.format(data.merchantPayable)}
            </span>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
