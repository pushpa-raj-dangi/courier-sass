"use client";

import Image from "next/image";
import { motion, useReducedMotion } from "framer-motion";

// All four marketing screenshots (public/marketing/screenshots/*.webp) are exported at this
// exact size, so a fixed intrinsic size here is correct for every caller, not an approximation
// — next/image needs it to reserve layout space up front (no CLS) since these are plain string
// paths, not statically-imported images with their own known dimensions.
const SCREENSHOT_WIDTH = 1568;
const SCREENSHOT_HEIGHT = 692;

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
  priority = false,
  className = "",
}: {
  src: string;
  alt: string;
  hoverTilt?: boolean;
  /** Set for the one above-the-fold instance (Hero); leave false everywhere else so next/image lazy-loads it. */
  priority?: boolean;
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
      <Image
        src={src}
        alt={alt}
        width={SCREENSHOT_WIDTH}
        height={SCREENSHOT_HEIGHT}
        priority={priority}
        sizes="100vw"
        className="h-auto w-full"
      />
    </motion.div>
  );
}
