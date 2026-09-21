"use client";

import type { ReactNode } from "react";
import { motion } from "framer-motion";
import { useRevealVariants } from "../lib/motionVariants";

/** Fades/raises its children in once on scroll; fully visible and static under reduced motion. */
export function Reveal({ children, className }: { children: ReactNode; className?: string }) {
  const { item } = useRevealVariants();
  return (
    <motion.div
      variants={item}
      initial="hidden"
      whileInView="visible"
      viewport={{ once: true, amount: 0.15 }}
      className={className}
    >
      {children}
    </motion.div>
  );
}
