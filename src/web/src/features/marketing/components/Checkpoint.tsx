"use client";

import { useRef } from "react";
import { useInView, useReducedMotion } from "framer-motion";

/** Numbered node pinned to the thread rail; lights up while its section is mid-viewport. */
export function Checkpoint({ index }: { index: number }) {
  const ref = useRef<HTMLSpanElement>(null);
  const reduced = useReducedMotion();
  const inView = useInView(ref, { margin: "-40% 0px -40% 0px" });
  const active = Boolean(reduced) || inView;

  return (
    <span
      ref={ref}
      data-testid="checkpoint"
      data-active={active}
      aria-hidden="true"
      className={`absolute left-[1.75rem] top-20 z-10 flex h-7 w-7 -translate-x-1/2 items-center justify-center rounded-sm border bg-marketing-bg font-mono text-[0.625rem] font-medium transition-colors duration-300 md:left-[2.75rem] md:top-28 md:h-8 md:w-8 md:text-[0.6875rem] ${
        active
          ? "border-marketing-accent text-marketing-accent"
          : "border-marketing-border text-marketing-muted-fg"
      }`}
    >
      {String(index).padStart(2, "0")}
    </span>
  );
}
