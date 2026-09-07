const NODE_POSITIONS = [
  { x: 14, y: 12 },
  { x: 56, y: 66 },
  { x: 10, y: 124 },
  { x: 52, y: 188 },
] as const;

const PATH_D = "M14,12 C46,26 18,50 56,66 C82,80 -6,96 10,124 C24,146 32,164 52,188";

// Literal HSL, not the shared status tokens: the tokens are tuned for legibility on
// --card/--background, and this graphic sits on the fixed auth-panel navy instead.
// Reusing --status-delivered directly here measured 2.5:1 against that navy (fails
// the 3:1 non-text floor) because dark mode's --status-delivered is tuned dark for a
// dark *card*, not for the panel's mid-dark navy. Each value below is the specific
// palette variant (light- or dark-mode token) that clears 3:1 against auth-panel's
// hsl(230 55% 25%) — verified with the dataviz skill's contrast validator — reused
// rather than invented, per the same hue family as the status it stands in for.
const TONE_COLOR: Record<AuthLifecycleTone, string> = {
  neutral: "hsl(234.3 13.2% 68.8%)", // --status-created, dark-mode variant
  motion: "hsl(35.3 71.2% 42.2%)", // --status-in-transit, light-mode variant
  highlight: "hsl(38.0 78.9% 57.3%)", // --status-out-for-delivery
  success: "hsl(169.7 47.9% 47.5%)", // --status-delivered, dark-mode variant
};

export type AuthLifecycleTone = "neutral" | "motion" | "highlight" | "success";

export interface AuthLifecycleStop {
  label: string;
  caption: string;
  tone: AuthLifecycleTone;
}

/**
 * The two auth surfaces' one deliberate graphic: a hand-drawn-feeling route line
 * threading four lifecycle stops, each tone borrowed from the real shipment/COD
 * status palette rather than invented for decoration. Draws in once on mount
 * (CSS animation only, `forwards` fill) and falls back to its fully-drawn resting
 * state under prefers-reduced-motion or before hydration.
 */
export function AuthLifecycleGraphic({ stops }: { stops: AuthLifecycleStop[] }) {
  return (
    <div className="flex h-40 items-stretch gap-4">
      <svg viewBox="0 0 70 200" className="h-full w-14 shrink-0" aria-hidden="true" focusable="false">
        <path
          d={PATH_D}
          fill="none"
          stroke="hsl(var(--auth-panel-foreground) / 0.25)"
          strokeWidth={2}
          strokeLinecap="round"
          strokeDasharray="3 6"
        />
        <path
          d={PATH_D}
          fill="none"
          stroke="hsl(var(--auth-panel-foreground) / 0.8)"
          strokeWidth={2}
          strokeLinecap="round"
          pathLength={100}
          strokeDasharray="100"
          strokeDashoffset={0}
          className="auth-graphic-draw"
        />
        {NODE_POSITIONS.map((pos, i) => (
          <circle
            key={pos.x + "-" + pos.y}
            cx={pos.x}
            cy={pos.y}
            r={4}
            fill={TONE_COLOR[stops[i]?.tone ?? "neutral"]}
            stroke="hsl(var(--auth-panel-foreground) / 0.5)"
            strokeWidth={1}
          />
        ))}
      </svg>
      <ul className="flex flex-1 flex-col justify-between">
        {stops.map((stop) => (
          <li key={stop.label}>
            <p className="text-sm font-medium text-auth-panel-foreground">{stop.label}</p>
            <p className="text-xs text-auth-panel-foreground/65">{stop.caption}</p>
          </li>
        ))}
      </ul>
    </div>
  );
}
