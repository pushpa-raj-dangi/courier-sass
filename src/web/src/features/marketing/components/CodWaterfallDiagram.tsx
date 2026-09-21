import { codBreakdown, formatNpr } from "../lib/diagramMath";
import { MonoLabel } from "./MonoLabel";

const H = 140;
const BASE = 180;

/** COD waterfall: collected − courier fee = merchant payable. Every bar is computed from the inputs. */
export function CodWaterfallDiagram({ collected = 2500, fee = 150 }: { collected?: number; fee?: number }) {
  const { payable } = codBreakdown(collected, fee);
  const scale = H / collected;
  const payableH = payable * scale;
  const feeH = H - payableH;

  const bars = [
    { x: 10, y: BASE - H, h: H, fill: "hsl(var(--marketing-accent))", lines: ["Collected"], value: formatNpr(collected) },
    {
      x: 140,
      y: BASE - H,
      h: feeH,
      fill: "hsl(var(--marketing-tone-neutral))",
      lines: ["Courier", "fee"],
      value: `− ${formatNpr(fee)}`,
    },
    {
      x: 270,
      y: BASE - payableH,
      h: payableH,
      fill: "hsl(var(--marketing-tone-success))",
      lines: ["Merchant", "payable"],
      value: formatNpr(payable),
    },
  ];

  return (
    <figure
      aria-label={`COD waterfall: ${formatNpr(collected)} collected minus ${formatNpr(fee)} courier fee equals ${formatNpr(payable)} merchant payable`}
      className="flex flex-col gap-4"
    >
      <svg
        aria-hidden="true"
        viewBox="0 0 360 232"
        className="h-auto w-full max-w-md"
        fontFamily="var(--font-plex-mono), monospace"
      >
        <line x1={0} y1={BASE} x2={360} y2={BASE} stroke="hsl(var(--marketing-border))" strokeWidth={1} />
        <line x1={90} y1={BASE - H} x2={140} y2={BASE - H} stroke="hsl(var(--marketing-border))" strokeDasharray="3 4" />
        <line
          x1={220}
          y1={BASE - payableH}
          x2={270}
          y2={BASE - payableH}
          stroke="hsl(var(--marketing-border))"
          strokeDasharray="3 4"
        />
        {bars.map((bar) => (
          <g key={bar.lines.join(" ")}>
            <rect x={bar.x} y={bar.y} width={80} height={Math.max(bar.h, 2)} rx={2} fill={bar.fill} />
            <text x={bar.x + 40} y={bar.y - 8} textAnchor="middle" fontSize={14} fontWeight={600} className="fill-marketing-fg">
              {bar.value}
            </text>
            {bar.lines.map((line, i) => (
              <text
                key={line}
                x={bar.x + 40}
                y={BASE + 20 + i * 16}
                textAnchor="middle"
                fontSize={13}
                fontFamily="var(--font-plex-sans), sans-serif"
                className="fill-marketing-muted-fg"
              >
                {line}
              </text>
            ))}
          </g>
        ))}
      </svg>
      <p className="sr-only">
        {formatNpr(collected)} collected, minus {formatNpr(fee)} courier fee, equals {formatNpr(payable)} merchant
        payable.
      </p>
      <figcaption>
        <MonoLabel>Sample data — illustrative amounts</MonoLabel>
      </figcaption>
    </figure>
  );
}
