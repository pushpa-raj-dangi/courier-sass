import { MANIFEST_SAMPLE } from "../content";
import { reconcileManifest } from "../lib/diagramMath";
import { MonoLabel } from "./MonoLabel";

const COLS = 12;
const PITCH = 30;
const CELL = 26;

/** Grid of expected shipments; each scanned cell is filled, each missing one is outlined and X-marked. */
export function ManifestReconcileDiagram() {
  const { code, from, to, expected, missingIndices } = MANIFEST_SAMPLE;
  const result = reconcileManifest(expected, [...missingIndices]);
  const missing = new Set(result.missingIndices);
  const rows = Math.ceil(expected / COLS);

  return (
    <figure
      aria-label={`Manifest ${code} from ${from} to ${to}: ${result.scanned} of ${result.expected} shipments scanned, ${result.missing} missing`}
      className="flex flex-col gap-4"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <MonoLabel className="text-marketing-fg">
          {code} · {from} → {to}
        </MonoLabel>
        <MonoLabel>Sample data</MonoLabel>
      </div>
      <svg
        aria-hidden="true"
        viewBox={`0 0 ${COLS * PITCH - (PITCH - CELL)} ${rows * PITCH - (PITCH - CELL)}`}
        className="h-auto w-full max-w-md"
      >
        {Array.from({ length: expected }, (_, i) => {
          const x = (i % COLS) * PITCH;
          const y = Math.floor(i / COLS) * PITCH;
          if (missing.has(i)) {
            return (
              <g key={i} data-cell data-cell-missing>
                <rect
                  x={x + 1}
                  y={y + 1}
                  width={CELL - 2}
                  height={CELL - 2}
                  rx={2}
                  fill="none"
                  stroke="hsl(var(--marketing-tone-alert))"
                  strokeWidth={2}
                />
                <path
                  data-cell-missing-mark
                  d={`M${x + 7},${y + 7} L${x + CELL - 7},${y + CELL - 7} M${x + CELL - 7},${y + 7} L${x + 7},${y + CELL - 7}`}
                  stroke="hsl(var(--marketing-tone-alert))"
                  strokeWidth={2}
                  strokeLinecap="round"
                />
              </g>
            );
          }
          return (
            <rect
              key={i}
              data-cell
              x={x}
              y={y}
              width={CELL}
              height={CELL}
              rx={2}
              fill="hsl(var(--marketing-accent))"
              fillOpacity={0.85}
            />
          );
        })}
      </svg>
      <figcaption className="flex flex-wrap items-center gap-x-6 gap-y-1">
        <MonoLabel className="text-marketing-fg">Expected {result.expected}</MonoLabel>
        <MonoLabel className="text-marketing-fg">Scanned {result.scanned}</MonoLabel>
        <MonoLabel className="text-marketing-fg">Missing {result.missing}</MonoLabel>
      </figcaption>
    </figure>
  );
}
