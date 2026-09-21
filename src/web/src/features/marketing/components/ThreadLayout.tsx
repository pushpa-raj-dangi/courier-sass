"use client";

import { useRef, type ReactNode } from "react";
import { motion, useReducedMotion, useScroll, useTransform } from "framer-motion";

/**
 * Wraps the checkpoint sections with one continuous vertical rail. The accent overlay's scaleY is
 * scroll-linked (no measured coordinates, so it survives any section height/width); under reduced
 * motion the rail renders fully drawn. Purely decorative, hence aria-hidden.
 */
export function ThreadLayout({ children }: { children: ReactNode }) {
  const ref = useRef<HTMLDivElement>(null);
  const reduced = useReducedMotion();
  const { scrollYProgress } = useScroll({ target: ref, offset: ["start 75%", "end 55%"] });
  const scaleY = useTransform(scrollYProgress, [0, 1], [0, 1]);

  return (
    <div ref={ref} className="relative">
      <div
        aria-hidden="true"
        data-testid="thread"
        className="pointer-events-none absolute inset-y-0 left-1/2 w-full max-w-6xl -translate-x-1/2"
      >
        <span className="absolute inset-y-0 left-[1.75rem] w-px bg-marketing-border/50 md:left-[2.75rem]" />
        <motion.span
          data-testid="thread-progress"
          className="absolute inset-y-0 left-[1.75rem] w-px origin-top bg-marketing-accent md:left-[2.75rem]"
          style={{ scaleY: reduced ? 1 : scaleY }}
        />
      </div>
      {children}
    </div>
  );
}
