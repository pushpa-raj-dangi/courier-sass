import { Check, AlertTriangle, Undo2, X } from "lucide-react";
import { cn } from "@/lib/utils";
import { buildProgress } from "@/features/shipments/lib/progress";

const EXCEPTION_LABEL = { ndr: "NDR", rto: "RTO", cancelled: "Cancelled" } as const;
const EXCEPTION_ICON = { ndr: AlertTriangle, rto: Undo2, cancelled: X } as const;

export function ProgressStepper({ status }: { status: string }) {
  const { steps, exception, exceptionAfter } = buildProgress(status);
  return (
    <ol aria-label="Progress" className="flex items-start gap-1">
      {steps.map((step, i) => (
        <li key={step.key} aria-current={step.state === "current" ? "step" : undefined} className="flex min-w-0 flex-1 flex-col items-center gap-1.5">
          <span
            className={cn(
              "flex h-[22px] w-[22px] items-center justify-center rounded-full border-2 text-[11px]",
              step.state === "complete" && "border-status-delivered bg-status-delivered text-white",
              step.state === "current" && "border-status-out-for-delivery bg-status-out-for-delivery",
              step.state === "upcoming" && "border-border bg-card"
            )}
          >
            {step.state === "complete" && <Check className="h-3 w-3" aria-hidden="true" />}
          </span>
          <span className={cn("text-center text-[10.5px] leading-tight", step.state === "upcoming" ? "text-muted-foreground" : "text-foreground")}>
            {step.label}
            <span className="sr-only">{` (${step.state})`}</span>
          </span>
          {exception && i === exceptionAfter && (
            <span className="flex items-center gap-1 rounded bg-destructive/10 px-1.5 text-[10.5px] font-medium text-destructive">
              {(() => {
                const Icon = EXCEPTION_ICON[exception];
                return <Icon className="h-3 w-3" aria-hidden="true" />;
              })()}
              {EXCEPTION_LABEL[exception]}
            </span>
          )}
        </li>
      ))}
    </ol>
  );
}
