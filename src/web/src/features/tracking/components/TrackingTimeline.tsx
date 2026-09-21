import { format, parseISO } from "date-fns";
import { resolveStatusConfig } from "@/components/status-badge/statusConfig";
import { STATUS_META, TRACKING_TONE_COLOR, toneAlpha } from "@/features/tracking/lib/statusMeta";
import type { PublicTrackingEvent } from "@/features/tracking/types";

// Events arrive oldest-first from the backend (PublicTrackingService orders by Timestamp
// ascending) — kept in that order here so the list reads top-to-bottom like a journey, ending
// at the current status closest to the summary hero above it. The final entry is the parcel's
// real current position, so it's the one node that glows.
export function TrackingTimeline({ events }: { events: PublicTrackingEvent[] }) {
  return (
    <ol className="space-y-0">
      {events.map((event, index) => {
        const isCurrent = index === events.length - 1;
        const meta = STATUS_META[event.status];
        const color = TRACKING_TONE_COLOR[meta.tone];
        const Icon = meta.icon;
        const label = resolveStatusConfig(event.status, "Public").label;

        return (
          <li key={`${event.status}-${event.timestamp}`} className="flex gap-4">
            <div className="flex flex-col items-center">
              <span
                className="flex h-8 w-8 shrink-0 items-center justify-center rounded-sm border"
                style={{
                  borderColor: color,
                  backgroundColor: isCurrent ? color : "hsl(var(--marketing-bg-elevated))",
                  boxShadow: isCurrent
                    ? `0 0 0 4px ${toneAlpha(meta.tone, 0.2)}`
                    : undefined,
                }}
                aria-hidden="true"
              >
                <Icon
                  className="h-4 w-4"
                  strokeWidth={2}
                  style={{ color: isCurrent ? "hsl(var(--marketing-bg))" : color }}
                />
              </span>
              {index < events.length - 1 && (
                <span className="w-px flex-1" style={{ backgroundColor: "hsl(var(--marketing-border))" }} aria-hidden="true" />
              )}
            </div>
            <div className="pb-8">
              <div className="flex flex-wrap items-center gap-2 pt-1">
                <span className="font-display text-sm font-semibold text-marketing-fg">{label}</span>
                {event.location && <span className="text-sm text-marketing-muted-fg">{event.location}</span>}
              </div>
              <p className="mt-1 font-mono text-xs text-marketing-muted-fg">
                {format(parseISO(event.timestamp), "d MMM yyyy, h:mm a")}
              </p>
            </div>
          </li>
        );
      })}
    </ol>
  );
}
