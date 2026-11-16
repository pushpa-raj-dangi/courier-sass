import { Badge } from "@/components/ui/badge";
import { riderStageLabel, riderStageToken, type RiderStage } from "@/features/riders/lib/riderModel";

/** Label + dot: the stage is never conveyed by colour alone. */
export function RiderStageBadge({ stage }: { stage: RiderStage }) {
  return (
    <Badge variant="outline" className="gap-1.5 whitespace-nowrap font-normal">
      <span className="h-1.5 w-1.5 shrink-0 rounded-full" style={{ backgroundColor: `hsl(var(--${riderStageToken(stage)}))` }} aria-hidden="true" />
      {riderStageLabel(stage)}
    </Badge>
  );
}

export function RiderInactiveBadge() {
  return (
    <Badge variant="secondary" className="font-normal">
      Inactive
    </Badge>
  );
}
