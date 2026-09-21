import type { ReactNode } from "react";
import { Checkpoint } from "./Checkpoint";
import { MonoLabel } from "./MonoLabel";
import { Reveal } from "./Reveal";

/**
 * One checkpoint section on the custody thread. The "elevated" tone fades a raised band in and out
 * (gradient, not a hard divider) so adjacent sections transition instead of stacking.
 */
export function SectionShell({
  id,
  index,
  eyebrow,
  title,
  lede,
  tone = "base",
  children,
}: {
  id: string;
  index: number;
  eyebrow: string;
  title: ReactNode;
  lede?: ReactNode;
  tone?: "base" | "elevated";
  children: ReactNode;
}) {
  return (
    <section
      id={id}
      aria-labelledby={`${id}-heading`}
      className={tone === "elevated" ? "bg-gradient-to-b from-transparent via-marketing-bg-elevated/70 to-transparent" : ""}
    >
      <div className="relative mx-auto max-w-6xl px-4 py-20 pl-14 md:py-28 md:pl-24">
        <Checkpoint index={index} />
        <MonoLabel>
          {String(index).padStart(2, "0")} — {eyebrow}
        </MonoLabel>
        <h2
          id={`${id}-heading`}
          className="mt-3 max-w-3xl font-display text-3xl font-semibold leading-[1.1] tracking-tight text-marketing-fg sm:text-4xl md:text-5xl"
        >
          {title}
        </h2>
        {lede && <p className="mt-4 max-w-2xl text-base text-marketing-muted-fg md:text-lg">{lede}</p>}
        <Reveal className="mt-10 md:mt-14">{children}</Reveal>
      </div>
    </section>
  );
}
