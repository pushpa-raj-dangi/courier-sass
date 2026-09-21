/** Decorative faint waybill grid, radially masked so it fades out — atmosphere for Hero and /track. */
export function MarketingGrid({ className = "" }: { className?: string }) {
  return (
    <div
      aria-hidden="true"
      data-testid="marketing-grid"
      className={`pointer-events-none absolute inset-0 -z-10 ${className}`}
      style={{
        backgroundImage:
          "linear-gradient(hsl(var(--marketing-border) / 0.18) 1px, transparent 1px), linear-gradient(90deg, hsl(var(--marketing-border) / 0.18) 1px, transparent 1px)",
        backgroundSize: "48px 48px",
        maskImage: "radial-gradient(ellipse 80% 70% at 60% 30%, black 20%, transparent 75%)",
        WebkitMaskImage: "radial-gradient(ellipse 80% 70% at 60% 30%, black 20%, transparent 75%)",
      }}
    />
  );
}
