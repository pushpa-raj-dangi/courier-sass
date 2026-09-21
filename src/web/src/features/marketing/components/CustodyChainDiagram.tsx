import { CUSTODY_CHAIN } from "../content";
import { MonoLabel } from "./MonoLabel";

const ACCENT = "hsl(var(--marketing-accent))";
const BORDER = "hsl(var(--marketing-border))";

function Node({ x, y, evidence }: { x: number; y: number; evidence: string }) {
  // Hollow = booking (no physical evidence yet); filled = physical scan or proof of delivery.
  return evidence === "booking" ? (
    <circle cx={x} cy={y} r={8} fill="hsl(var(--marketing-bg))" stroke={ACCENT} strokeWidth={2} />
  ) : (
    <circle cx={x} cy={y} r={8} fill={ACCENT} />
  );
}

/**
 * The 8-step lifecycle as a chain of custody. Horizontal from lg up, vertical below (the 8-column
 * layout is unreadable at tablet widths). Both SVGs are decorative — the sr-only list carries the content.
 */
export function CustodyChainDiagram() {
  const n = CUSTODY_CHAIN.length;
  const stepX = 120;
  const x0 = 60;
  const stepY = 64;

  return (
    <figure
      aria-label="Custody chain: eight lifecycle steps from booking to proof of delivery"
      className="flex flex-col gap-6"
    >
      <svg
        aria-hidden="true"
        viewBox="0 0 960 130"
        className="hidden h-auto w-full lg:block"
        fontFamily="var(--font-plex-sans), sans-serif"
      >
        <line x1={x0} y1={40} x2={x0 + stepX * (n - 1)} y2={40} stroke={BORDER} strokeWidth={2} />
        {CUSTODY_CHAIN.map((step, i) => {
          const x = x0 + i * stepX;
          return (
            <g key={step.label}>
              <Node x={x} y={40} evidence={step.evidence} />
              <text x={x} y={72} textAnchor="middle" fontSize={13} fontWeight={600} className="fill-marketing-fg">
                {step.label}
              </text>
              <text x={x} y={90} textAnchor="middle" fontSize={12} className="fill-marketing-muted-fg">
                {step.holder}
              </text>
              <text
                x={x}
                y={112}
                textAnchor="middle"
                fontSize={10}
                letterSpacing={1}
                fontFamily="var(--font-plex-mono), monospace"
                className="fill-marketing-accent"
              >
                {step.evidenceLabel}
              </text>
            </g>
          );
        })}
      </svg>

      <svg
        aria-hidden="true"
        viewBox={`0 0 320 ${n * stepY}`}
        className="mx-auto h-auto w-full max-w-sm lg:hidden"
        fontFamily="var(--font-plex-sans), sans-serif"
      >
        <line x1={20} y1={32} x2={20} y2={32 + stepY * (n - 1)} stroke={BORDER} strokeWidth={2} />
        {CUSTODY_CHAIN.map((step, i) => {
          const y = 32 + i * stepY;
          return (
            <g key={step.label}>
              <Node x={20} y={y} evidence={step.evidence} />
              <text x={44} y={y - 2} fontSize={15} fontWeight={600} className="fill-marketing-fg">
                {step.label}
              </text>
              <text x={44} y={y + 15} fontSize={12} className="fill-marketing-muted-fg">
                {step.holder}
              </text>
              <text
                x={44}
                y={y + 30}
                fontSize={10}
                letterSpacing={1}
                fontFamily="var(--font-plex-mono), monospace"
                className="fill-marketing-accent"
              >
                {step.evidenceLabel}
              </text>
            </g>
          );
        })}
      </svg>

      <ol className="sr-only">
        {CUSTODY_CHAIN.map((step) => (
          <li key={step.label}>
            {step.label} — held by {step.holder} ({step.evidenceLabel})
          </li>
        ))}
      </ol>

      <figcaption className="flex flex-wrap items-center gap-x-6 gap-y-2">
        <MonoLabel>Filled node = scan-verified or proof of delivery</MonoLabel>
        <MonoLabel>Hollow node = booking, no physical evidence yet</MonoLabel>
      </figcaption>
    </figure>
  );
}
