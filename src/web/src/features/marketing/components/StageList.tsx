/** Vertical rail of ordered lifecycle stages (COD / NDR / RTO); the rail echoes the page's thread. */
export function StageList({ label, stages }: { label: string; stages: string[] }) {
  return (
    <ol aria-label={label} className="relative flex flex-col gap-3 border-l border-marketing-border pl-5">
      {stages.map((stage, index) => (
        <li
          key={stage}
          className="relative font-mono text-xs uppercase tracking-[0.12em] text-marketing-muted-fg transition-colors hover:text-marketing-fg"
        >
          <span
            aria-hidden="true"
            className="absolute -left-[1.6875rem] top-1/2 h-2 w-2 -translate-y-1/2 rounded-sm bg-marketing-accent"
          />
          <span className="mr-2 text-marketing-accent">{String(index + 1).padStart(2, "0")}</span>
          {stage}
        </li>
      ))}
    </ol>
  );
}
