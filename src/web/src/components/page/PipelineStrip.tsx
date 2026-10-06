import type { ReactNode } from "react";
import Link from "next/link";

export interface PipelineStage {
  key: string;
  label: string;
  /** null = unknown (data unavailable) — rendered as an em dash, never as 0. */
  count: number | null;
  /** A statusConfig token name, e.g. "status-in-transit". */
  token: string;
  /** 0–1; clamped. Omit to hide the bar. */
  share?: number;
  caption?: ReactNode;
  href?: string;
}

const TILE =
  "flex h-full flex-col gap-2 rounded-lg border border-border bg-background px-3.5 py-3 text-foreground transition-colors";

function StageBody({ stage }: { stage: PipelineStage }) {
  const share = stage.share === undefined ? undefined : Math.min(1, Math.max(0, stage.share));
  return (
    <>
      <span className="flex items-center gap-1.5 text-xs text-muted-foreground">
        <span className="h-2 w-2 shrink-0 rounded-full" style={{ backgroundColor: `hsl(var(--${stage.token}))` }} aria-hidden="true" />
        {stage.label}
      </span>
      <span className="text-2xl font-semibold tabular-nums tracking-tight">{stage.count === null ? "—" : stage.count.toLocaleString("en-US")}</span>
      {share !== undefined && (
        <span className="block h-1 rounded-full bg-muted" aria-hidden="true">
          <span
            data-share-bar={stage.key}
            className="block h-1 rounded-full"
            style={{ width: `${Math.round(share * 100)}%`, backgroundColor: `hsl(var(--${stage.token}))` }}
          />
        </span>
      )}
      {stage.caption && <span className="text-[11.5px] text-muted-foreground">{stage.caption}</span>}
    </>
  );
}

export function PipelineStrip({ stages, ariaLabel }: { stages: PipelineStage[]; ariaLabel: string }) {
  return (
    <ol aria-label={ariaLabel} className="grid grid-cols-2 gap-2.5 sm:grid-cols-4 xl:auto-cols-fr xl:grid-flow-col xl:grid-cols-none">
      {stages.map((stage) => (
        <li key={stage.key}>
          {stage.href ? (
            <Link href={stage.href} className={`${TILE} hover:bg-accent`}>
              <StageBody stage={stage} />
            </Link>
          ) : (
            <div className={TILE}>
              <StageBody stage={stage} />
            </div>
          )}
        </li>
      ))}
    </ol>
  );
}
