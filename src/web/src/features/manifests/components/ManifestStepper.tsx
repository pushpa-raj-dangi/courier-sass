import { Check, Circle, CircleDot } from "lucide-react";
import { cn } from "@/lib/utils";
import { MANIFEST_STAGES, stepperState } from "@/features/manifests/lib/manifestModel";
import type { ManifestStatus } from "@/features/manifests/types";

const STATE_TEXT = { complete: "done", current: "current stage", upcoming: "not yet" } as const;

// Status-only: the backend stores no stage timestamps (follow-up 1), so each step says done /
// current / not yet by icon and text, never colour alone.
export function ManifestStepper({ status }: { status: ManifestStatus }) {
  return (
    <div className="space-y-2">
      <ol aria-label="Manifest progress" className="grid grid-cols-5 gap-2">
        {MANIFEST_STAGES.map((stage) => {
          const state = stepperState(status, stage.status);
          const Icon = state === "complete" ? Check : state === "current" ? CircleDot : Circle;
          return (
            <li key={stage.status} aria-current={state === "current" ? "step" : undefined} className="flex flex-col items-center gap-1 text-center">
              <Icon className={cn("h-4 w-4", state === "upcoming" ? "text-muted-foreground" : "text-foreground")} aria-hidden="true" />
              <span className={cn("text-2xs", state === "upcoming" ? "text-muted-foreground" : "font-medium text-foreground")}>{stage.label}</span>
              <span className="sr-only">{STATE_TEXT[state]}</span>
            </li>
          );
        })}
      </ol>
      <p data-coming-soon="" className="text-2xs text-muted-foreground">Stage times need backend support.</p>
    </div>
  );
}
