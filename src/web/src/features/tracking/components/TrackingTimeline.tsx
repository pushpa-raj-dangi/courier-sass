import { format, parseISO } from "date-fns";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import type { PublicTrackingEvent } from "@/features/tracking/types";

// Events arrive oldest-first from the backend (PublicTrackingService orders by Timestamp
// ascending) — kept in that order here so the list reads top-to-bottom like a journey, ending
// at the current status closest to the summary badge above it.
export function TrackingTimeline({ events }: { events: PublicTrackingEvent[] }) {
  return (
    <ol className="space-y-0">
      {events.map((event, index) => (
        <li key={`${event.status}-${event.timestamp}`} className="flex gap-4">
          <div className="flex flex-col items-center">
            <span className="mt-1 h-2.5 w-2.5 shrink-0 rounded-full bg-primary" aria-hidden="true" />
            {index < events.length - 1 && <span className="w-px flex-1 bg-border" aria-hidden="true" />}
          </div>
          <div className="pb-6">
            <div className="flex flex-wrap items-center gap-2">
              <StatusBadge status={event.status} namespace="Public" />
              {event.location && <span className="text-sm text-muted-foreground">{event.location}</span>}
            </div>
            <p className="mt-1 text-xs text-muted-foreground">
              {format(parseISO(event.timestamp), "d MMM yyyy, h:mm a")}
            </p>
          </div>
        </li>
      ))}
    </ol>
  );
}
