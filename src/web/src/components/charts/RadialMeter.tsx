// A single-ratio meter (dataviz skill: "a single ratio against a limit -> Meter, not a
// pie of 2 slices"). The track is the same hue as the fill at a lighter step (~15%
// opacity), so the whole ring reads as one state rather than two competing colors.
export function RadialMeter({
  value,
  colorToken,
  size = 168,
  strokeWidth = 14,
  label,
}: {
  /** 0-1, clamped */
  value: number | null;
  colorToken: string;
  size?: number;
  strokeWidth?: number;
  label: string;
}) {
  const clamped = value === null ? 0 : Math.min(1, Math.max(0, value));
  const radius = (size - strokeWidth) / 2;
  const circumference = 2 * Math.PI * radius;
  const dashOffset = circumference * (1 - clamped);
  const center = size / 2;

  return (
    <div className="flex flex-col items-center gap-2" role="img" aria-label={`${label}: ${value === null ? "no data" : `${Math.round(clamped * 100)}%`}`}>
      <div className="relative" style={{ width: size, height: size }}>
        <svg width={size} height={size} className="-rotate-90">
          <circle
            cx={center}
            cy={center}
            r={radius}
            fill="none"
            stroke={`hsl(var(--${colorToken}) / 0.15)`}
            strokeWidth={strokeWidth}
          />
          <circle
            cx={center}
            cy={center}
            r={radius}
            fill="none"
            stroke={`hsl(var(--${colorToken}))`}
            strokeWidth={strokeWidth}
            strokeLinecap="round"
            strokeDasharray={circumference}
            strokeDashoffset={dashOffset}
            className="transition-[stroke-dashoffset]"
          />
        </svg>
        <div className="absolute inset-0 flex items-center justify-center">
          <span className="font-sans text-3xl font-semibold text-foreground">
            {value === null ? "—" : `${(clamped * 100).toFixed(1)}%`}
          </span>
        </div>
      </div>
      <span className="text-sm text-muted-foreground">{label}</span>
    </div>
  );
}
