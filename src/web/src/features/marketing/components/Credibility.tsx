"use client";

import { motion } from "framer-motion";
import { CREDIBILITY_POINTS } from "../content";
import { useRevealVariants } from "../lib/motionVariants";

export function Credibility() {
  const { container, item } = useRevealVariants();

  return (
    <section className="mx-auto max-w-6xl px-4 py-24">
      <div className="mb-12 flex flex-col items-center gap-3 text-center">
        <h2 className="font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">
          Built for how Nepal courier operations actually work
        </h2>
        <p className="max-w-2xl text-marketing-muted-fg">
          No case studies yet — this is still an early platform. What's already true, today:
        </p>
      </div>
      <motion.div
        variants={container}
        initial="hidden"
        whileInView="visible"
        viewport={{ once: true, amount: 0.2 }}
        className="grid grid-cols-1 gap-6 sm:grid-cols-2"
      >
        {CREDIBILITY_POINTS.map((point) => (
          <motion.div
            key={point.title}
            variants={item}
            className="rounded-xl border border-marketing-border bg-marketing-bg-elevated p-6"
          >
            <h3 className="font-display text-base font-semibold text-marketing-fg">{point.title}</h3>
            <p className="mt-2 text-sm text-marketing-muted-fg">{point.description}</p>
          </motion.div>
        ))}
      </motion.div>
    </section>
  );
}
