import { cn } from "@/lib/utils";
import { formatEventTime, type JourneyStep } from "@/features/tracking/lib/journey";
import { statusTokenClasses } from "@/features/tracking/lib/statusTokenClasses";

function stepDotClass(step: JourneyStep, nowToken: string): string {
  if (step.state === "done") return "border-primary bg-primary";
  if (step.state === "next") return "border-border bg-card";
  const t = statusTokenClasses(step.alert ? "status-ndr" : nowToken);
  return cn(t.dot, t.ring);
}

export function TrackingJourney({ steps, nowToken = "status-in-transit" }: { steps: JourneyStep[]; nowToken?: string }) {
  return (
    <section aria-label="Journey" className="mt-6">
      <ol>
        {steps.map((step, index) => {
          const isLast = index === steps.length - 1;
          const meta = step.timestamp
            ? [formatEventTime(step.timestamp), step.location].filter(Boolean).join(" · ")
            : null;
          return (
            <li key={step.stage} data-state={step.state} className="relative flex gap-3 pb-5 last:pb-0">
              {!isLast && (
                <span
                  aria-hidden="true"
                  className={cn(
                    "absolute left-[9px] top-5 h-[calc(100%-0.75rem)] w-0.5",
                    step.state === "done" ? "bg-primary" : "bg-border"
                  )}
                />
              )}
              <span
                aria-hidden="true"
                className={cn("mt-0.5 size-5 shrink-0 rounded-full border-2", stepDotClass(step, nowToken))}
              />
              <div className="min-w-0">
                <p
                  className={cn(
                    "text-sm",
                    step.state === "next" ? "text-muted-foreground" : "font-medium text-foreground"
                  )}
                >
                  {step.label}
                </p>
                {step.state !== "next" && meta && <p className="text-xs text-muted-foreground">{meta}</p>}
              </div>
            </li>
          );
        })}
      </ol>
    </section>
  );
}
