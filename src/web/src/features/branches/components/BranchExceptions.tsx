import { AlertTriangle } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

const currencyFormatter = new Intl.NumberFormat("en-NP", {
  style: "currency",
  currency: "NPR",
  notation: "compact",
  maximumFractionDigits: 1,
});

export function BranchExceptions({
  data,
}: {
  data: {
    missingShipmentExceptions: number;
    openNdrCount: number;
    riders: Array<{ riderId: string; openTaskCount: number }>;
    codPendingFinanceVerification: number;
  };
}) {
  const totalOpenTasks = data.riders.reduce((sum, r) => sum + r.openTaskCount, 0);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-3 font-display text-base">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-muted">
            <AlertTriangle className="h-4 w-4 text-muted-foreground" strokeWidth={2} />
          </span>
          Exceptions
        </CardTitle>
        <CardDescription>Needs attention at this branch</CardDescription>
      </CardHeader>
      <CardContent className="grid grid-cols-2 gap-6 sm:grid-cols-4">
        <div className="flex flex-col gap-1">
          <span className="text-xs text-muted-foreground">Missing Shipments</span>
          <span className="font-sans text-2xl font-semibold text-foreground">
            {data.missingShipmentExceptions}
          </span>
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-xs text-muted-foreground">Open NDRs</span>
          <span className="font-sans text-2xl font-semibold text-status-ndr">{data.openNdrCount}</span>
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-xs text-muted-foreground">Riders</span>
          <span className="font-sans text-2xl font-semibold text-foreground">
            {data.riders.length} riders
          </span>
          <span className="text-xs text-muted-foreground">{totalOpenTasks} open tasks</span>
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-xs text-muted-foreground">COD Pending Verification</span>
          <span className="font-sans text-2xl font-semibold text-foreground">
            {currencyFormatter.format(data.codPendingFinanceVerification)}
          </span>
        </div>
      </CardContent>
    </Card>
  );
}
