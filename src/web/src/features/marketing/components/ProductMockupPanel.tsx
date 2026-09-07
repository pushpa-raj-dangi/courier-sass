import { TONE_COLOR } from "../lib/toneColor";
import type { WalkthroughStop } from "../content";

/**
 * The stylized dashboard/tracking mockup the F13 design spec called for as the
 * hero's opening frame and the walkthrough's centerpiece (§4.2/§4.5) — reusing the
 * app's real StatusBadge grammar (a colored dot always paired with its text label,
 * never color alone) and status-token colors rather than inventing new marketing
 * decoration. All data shown is placeholder-style ("AWB NP2026-000000") per
 * PRODUCT.md's no-fabricated-evidence constraint — never formatted to resemble a
 * real customer's shipment.
 */
export function ProductMockupPanel({
  stops,
  activeIndex,
  size = "full",
}: {
  stops: WalkthroughStop[];
  activeIndex: number;
  size?: "compact" | "full";
}) {
  const activeStop = stops[activeIndex];

  return (
    <div className="w-full overflow-hidden rounded-xl border border-marketing-border bg-marketing-bg-elevated">
      <div className="flex items-center gap-2 border-b border-marketing-border/60 px-4 py-3">
        <span className="h-1.5 w-1.5 shrink-0 animate-pulse rounded-full bg-marketing-accent" aria-hidden="true" />
        <p className="font-mono text-[11px] text-marketing-muted-fg">AWB NP2026-000000</p>
        <p className="ml-auto text-[10px] uppercase tracking-wide text-marketing-muted-fg">Tracking preview</p>
      </div>

      <div className={size === "compact" ? "flex flex-col gap-4 p-5" : "flex flex-col gap-6 p-6 sm:p-8"}>
        <div className="flex flex-col gap-1.5">
          <div className="flex items-center gap-2">
            <span
              className="h-2 w-2 shrink-0 rounded-full"
              style={{ backgroundColor: TONE_COLOR[activeStop.tone] }}
              aria-hidden="true"
            />
            <p className={size === "compact" ? "text-sm font-medium text-marketing-fg" : "font-display text-lg font-semibold text-marketing-fg"}>
              {activeStop.label}
            </p>
          </div>
          <p className="text-xs text-marketing-muted-fg">{activeStop.caption}</p>
        </div>

        <div className="flex items-center" aria-hidden="true">
          {stops.map((stop, index) => {
            const isLast = index === stops.length - 1;
            const isComplete = index <= activeIndex;
            return (
              <div key={stop.label} className="flex flex-1 items-center last:flex-none">
                <span
                  className="h-2 w-2 shrink-0 rounded-full transition-colors"
                  style={{
                    backgroundColor: isComplete ? TONE_COLOR[stop.tone] : "hsl(var(--marketing-border))",
                  }}
                />
                {!isLast && (
                  <span
                    className="mx-1.5 h-px flex-1 transition-colors"
                    style={{
                      backgroundColor: index < activeIndex ? TONE_COLOR[stops[index + 1].tone] : "hsl(var(--marketing-border))",
                    }}
                  />
                )}
              </div>
            );
          })}
        </div>
        <div className="flex justify-between">
          {stops.map((stop, index) => (
            <span
              key={stop.label}
              className={
                index === activeIndex
                  ? "text-[10px] font-medium text-marketing-fg"
                  : "text-[10px] text-marketing-muted-fg"
              }
            >
              {stop.label}
            </span>
          ))}
        </div>

        {size === "full" && (
          <div className="grid grid-cols-2 gap-4 border-t border-marketing-border/60 pt-5 font-mono text-[11px]">
            <div className="flex flex-col gap-1">
              <span className="text-marketing-muted-fg">Route</span>
              <span className="text-marketing-fg">Kathmandu Hub → Pokhara Hub</span>
            </div>
            <div className="flex flex-col gap-1">
              <span className="text-marketing-muted-fg">Service</span>
              <span className="text-marketing-fg">Standard, COD</span>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
