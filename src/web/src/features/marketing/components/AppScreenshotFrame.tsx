"use client";

import { motion, useReducedMotion } from "framer-motion";

/**
 * Wraps a real app screenshot in a browser-chrome-style frame with an
 * accent glow, replacing the stylized ProductMockupPanel this redesign
 * removes. hoverTilt adds a subtle 3D tilt on hover, gated behind
 * prefers-reduced-motion like every other animated piece on this page.
 */
export function AppScreenshotFrame({
  src,
  alt,
  hoverTilt = false,
  className = "",
}: {
  src: string;
  alt: string;
  hoverTilt?: boolean;
  className?: string;
}) {
  const prefersReducedMotion = useReducedMotion();
  const tiltEnabled = hoverTilt && !prefersReducedMotion;

  return (
    <motion.div
      data-testid="app-screenshot-frame"
      whileHover={tiltEnabled ? { rotateX: 4, rotateY: -4, scale: 1.01 } : undefined}
      transition={{ type: "spring", stiffness: 220, damping: 18 }}
      style={{ transformPerspective: 900 }}
      className={`relative overflow-hidden rounded-xl border border-marketing-border bg-marketing-bg-elevated shadow-[0_0_60px_-15px_hsl(var(--marketing-accent)/0.5)] ${className}`}
    >
      <div className="flex items-center gap-1.5 border-b border-marketing-border/60 px-4 py-3" aria-hidden="true">
        <span className="h-2.5 w-2.5 rounded-full bg-marketing-border" />
        <span className="h-2.5 w-2.5 rounded-full bg-marketing-border" />
        <span className="h-2.5 w-2.5 rounded-full bg-marketing-border" />
      </div>
      <img src={src} alt={alt} className="w-full" />
    </motion.div>
  );
}
