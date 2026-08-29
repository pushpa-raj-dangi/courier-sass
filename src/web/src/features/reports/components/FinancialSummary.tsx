import { Wallet } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import type { CodFinancialSummary } from "@/features/reports/types";

const currencyFormatter = new Intl.NumberFormat("en-NP", {
  style: "currency",
  currency: "NPR",
  notation: "compact",
  maximumFractionDigits: 1,
});

const FIELDS: Array<{ key: keyof CodFinancialSummary; label: string }> = [
  { key: "expected", label: "Expected" },
  { key: "collected", label: "Collected" },
  { key: "pending", label: "Pending" },
  { key: "settled", label: "Settled" },
  { key: "merchantPayable", label: "Merchant Payable" },
];

export function FinancialSummary({ data }: { data: CodFinancialSummary }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 font-display text-base">
          <Wallet className="h-4 w-4 text-muted-foreground" strokeWidth={2} />
          COD Summary
        </CardTitle>
      </CardHeader>
      <CardContent className="grid grid-cols-2 gap-6 sm:grid-cols-5">
        {FIELDS.map((field) => (
          <div key={field.key} className="flex flex-col gap-1">
            <span className="text-xs text-muted-foreground">{field.label}</span>
            <span className="font-sans text-lg font-semibold text-foreground">
              {currencyFormatter.format(data[field.key])}
            </span>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}
