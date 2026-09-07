"use client";

import { useReducedMotion } from "framer-motion";
import type { Variants } from "framer-motion";

export const containerVariants: Variants = {
  hidden: {},
  visible: { transition: { staggerChildren: 0.12 } },
};

export const itemVariants: Variants = {
  hidden: { opacity: 0, y: 24 },
  visible: { opacity: 1, y: 0, transition: { duration: 0.5, ease: "easeOut" } },
};

const staticItemVariants: Variants = {
  hidden: { opacity: 1, y: 0 },
  visible: { opacity: 1, y: 0 },
};

export interface RevealVariants {
  container: Variants;
  item: Variants;
  prefersReducedMotion: boolean;
}

/**
 * Shared scroll-reveal variants for every marketing section. Falls back to a
 * fully-visible static state under prefers-reduced-motion, mirroring the
 * fallback AuthLifecycleGraphic already established for the two auth surfaces.
 */
export function useRevealVariants(): RevealVariants {
  const prefersReducedMotion = useReducedMotion();
  return {
    container: containerVariants,
    item: prefersReducedMotion ? staticItemVariants : itemVariants,
    prefersReducedMotion: Boolean(prefersReducedMotion),
  };
}
