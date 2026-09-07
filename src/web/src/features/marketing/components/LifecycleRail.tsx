import { TONE_COLOR } from "../lib/toneColor";
import type { WalkthroughTone } from "../content";

export interface LifecycleRailStop {
  tone: WalkthroughTone;
  label: string;
}

const DOT_SIZE: Record<"sm" | "md" | "lg", string> = {
  sm: "h-1.5 w-1.5",
  md: "h-2 w-2",
  lg: "h-2.5 w-2.5",
};

/**
 * Shared dot-and-connecting-line rail for the courier lifecycle's four stages.
 * Reused by the hero/walkthrough mockup (progress mode: activeIndex set, only
 * stages up to it are lit) and HowItWorks (legend mode: activeIndex omitted,
 * every stage shows its own tone) so the same tone-coded device carries the
 * lifecycle story across sections instead of each section inventing its own copy.
 */
export function LifecycleRail({
  stops,
  activeIndex,
  showLabels = false,
  dotSize = "md",
}: {
  stops: LifecycleRailStop[];
  activeIndex?: number;
  showLabels?: boolean;
  dotSize?: "sm" | "md" | "lg";
}) {
  return (
    <div>
      <div className="flex items-center" aria-hidden="true">
        {stops.map((stop, index) => {
          const isLast = index === stops.length - 1;
          const isReached = activeIndex === undefined || index <= activeIndex;
          const lineReached = activeIndex === undefined || index < activeIndex;
          return (
            <div key={stop.label} className="flex flex-1 items-center last:flex-none">
              <span
                className={`${DOT_SIZE[dotSize]} shrink-0 rounded-full transition-colors`}
                style={{ backgroundColor: isReached ? TONE_COLOR[stop.tone] : "hsl(var(--marketing-border))" }}
              />
              {!isLast && (
                <span
                  className="mx-1.5 h-px flex-1 transition-colors"
                  style={{
                    backgroundColor: lineReached ? TONE_COLOR[stops[index + 1].tone] : "hsl(var(--marketing-border))",
                    opacity: activeIndex === undefined ? 0.4 : 1,
                  }}
                />
              )}
            </div>
          );
        })}
      </div>
      {showLabels && (
        <div className="mt-2 flex justify-between">
          {stops.map((stop, index) => (
            <span
              key={stop.label}
              className={
                index === activeIndex ? "text-[10px] font-medium text-marketing-fg" : "text-[10px] text-marketing-muted-fg"
              }
            >
              {stop.label}
            </span>
          ))}
        </div>
      )}
    </div>
  );
}
