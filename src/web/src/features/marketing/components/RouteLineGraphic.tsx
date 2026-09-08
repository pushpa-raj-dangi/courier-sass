"use client";

import { motion, useReducedMotion } from "framer-motion";

/**
 * Purely decorative dashed route line with hub nodes, evoking a delivery
 * route between branches — background texture for Hero/HowItWorks, never
 * informational, so it's aria-hidden.
 */
export function RouteLineGraphic({
  className = "",
  strokeOpacity = 0.25,
  hubOpacity = 0.4,
}: {
  className?: string;
  /** Line opacity — Hero/HowItWorks keep the 0.25 default; a page where this graphic carries
   * more of the atmosphere (e.g. /track) can pass a higher value instead of stacking a wrapper
   * `opacity-*` class on top, which would multiply against this and read as near-invisible. */
  strokeOpacity?: number;
  hubOpacity?: number;
}) {
  const prefersReducedMotion = useReducedMotion();
  const hubs: Array<[number, number]> = [
    [0, 150],
    [350, 50],
    [700, 100],
  ];

  return (
    <svg
      aria-hidden="true"
      data-testid="route-line-graphic"
      viewBox="0 0 800 200"
      preserveAspectRatio="none"
      className={`pointer-events-none ${className}`}
    >
      <motion.path
        d="M0,150 C150,150 200,50 350,50 S550,150 700,100 S800,40 800,40"
        fill="none"
        stroke="hsl(var(--marketing-accent))"
        strokeOpacity={strokeOpacity}
        strokeWidth={1.5}
        strokeDasharray="6 10"
        animate={prefersReducedMotion ? undefined : { strokeDashoffset: [0, -64] }}
        transition={prefersReducedMotion ? undefined : { duration: 6, repeat: Infinity, ease: "linear" }}
      />
      {hubs.map(([cx, cy]) => (
        <circle key={`${cx}-${cy}`} cx={cx} cy={cy} r={4} fill="hsl(var(--marketing-accent))" fillOpacity={hubOpacity} />
      ))}
    </svg>
  );
}
