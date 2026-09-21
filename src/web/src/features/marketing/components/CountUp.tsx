"use client";

import { useEffect, useRef } from "react";
import { animate, useInView, useReducedMotion } from "framer-motion";

const format = (n: number) => n.toLocaleString("en-US");

/**
 * Counts up once when scrolled into view. The initial markup is the final value so no-JS, SSR and
 * reduced-motion users always see the real number; animation only rewrites text content in place.
 */
export function CountUp({ value, className }: { value: number; className?: string }) {
  const ref = useRef<HTMLSpanElement>(null);
  const reduced = useReducedMotion();
  const inView = useInView(ref, { once: true });

  useEffect(() => {
    const node = ref.current;
    if (reduced || !inView || !node) return;
    const controls = animate(0, value, {
      duration: 0.9,
      ease: "easeOut",
      onUpdate: (latest) => {
        node.textContent = format(Math.round(latest));
      },
    });
    return () => controls.stop();
  }, [inView, reduced, value]);

  return (
    <span ref={ref} className={className}>
      {format(value)}
    </span>
  );
}
