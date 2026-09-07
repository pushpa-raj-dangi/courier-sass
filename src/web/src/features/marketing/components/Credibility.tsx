import { CREDIBILITY_POINTS } from "../content";

export function Credibility() {
  return (
    <section className="mx-auto max-w-3xl px-4 py-24">
      <div className="mb-10 flex flex-col items-center gap-3 text-center">
        <h2 className="font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">
          Built for how Nepal courier operations actually work
        </h2>
        <p className="max-w-2xl text-marketing-muted-fg">
          No case studies yet — this is still an early platform. What&apos;s already true, today:
        </p>
      </div>
      <div className="flex flex-col divide-y divide-marketing-border/60 border-y border-marketing-border/60">
        {CREDIBILITY_POINTS.map((point) => (
          <div key={point.title} className="flex flex-col gap-1.5 py-6 sm:flex-row sm:items-baseline sm:gap-6">
            <h3 className="font-display text-base font-semibold text-marketing-fg sm:w-56 sm:shrink-0">
              {point.title}
            </h3>
            <p className="text-sm text-marketing-muted-fg">{point.description}</p>
          </div>
        ))}
      </div>
    </section>
  );
}
