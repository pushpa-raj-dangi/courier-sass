import { resolveStatusConfig } from "@/components/status-badge/statusConfig";
import { buildJourney } from "@/features/tracking/lib/journey";
import { STATUS_META } from "@/features/tracking/lib/statusMeta";
import { statusTokenClasses } from "@/features/tracking/lib/statusTokenClasses";
import { TrackingJourney } from "@/features/tracking/components/TrackingJourney";
import type { PublicTrackingResult } from "@/features/tracking/types";

export function TrackingStatusCard({ result }: { result: PublicTrackingResult }) {
  const { label, token } = resolveStatusConfig(result.currentStatus, "Public");
  const classes = statusTokenClasses(token);
  const route = [
    result.destinationArea ? `To ${result.destinationArea}` : null,
    result.senderName ? `from ${result.senderName}` : null,
  ]
    .filter(Boolean)
    .join(" · ");

  return (
    <div className="rounded-lg border border-border bg-card p-5 shadow-[6px_6px_0_0_hsl(var(--primary)/0.18)]">
      <p className={`flex items-center gap-2 text-xs font-medium uppercase tracking-wide ${classes.text}`}>
        <span aria-hidden="true" className={`size-2 rounded-full ${classes.dot}`} />
        {label}
      </p>
      <h1 className="mt-2 font-display text-3xl font-semibold tracking-tight text-foreground">
        {STATUS_META[result.currentStatus].headline}
      </h1>
      {route && <p className="mt-1 text-sm text-muted-foreground">{route}</p>}
      <TrackingJourney steps={buildJourney(result)} nowToken={token} />
    </div>
  );
}
