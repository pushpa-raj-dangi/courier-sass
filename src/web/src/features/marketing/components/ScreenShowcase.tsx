"use client";

import { motion } from "framer-motion";
import { SCREENSHOTS } from "../content";
import { useRevealVariants } from "../lib/motionVariants";
import { AppScreenshotFrame } from "./AppScreenshotFrame";

export function ScreenShowcase() {
  const { container, item } = useRevealVariants();

  return (
    <section id="screens" className="mx-auto max-w-6xl px-4 py-24">
      <div className="mb-12 flex flex-col items-center gap-3 text-center">
        <h2 className="font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">
          The actual product, not a mockup
        </h2>
        <p className="max-w-2xl text-marketing-muted-fg">
          Every screen below is the real app, running against real data — nothing hand-drawn.
        </p>
      </div>
      <motion.div
        variants={container}
        initial="hidden"
        whileInView="visible"
        viewport={{ once: true, amount: 0.2 }}
        className="grid grid-cols-1 gap-6 sm:grid-cols-2"
      >
        {SCREENSHOTS.map((screenshot) => (
          <motion.figure key={screenshot.id} variants={item} className="flex flex-col gap-3">
            <AppScreenshotFrame src={screenshot.src} alt={screenshot.alt} hoverTilt />
            <figcaption className="text-sm text-marketing-muted-fg">{screenshot.caption}</figcaption>
          </motion.figure>
        ))}
      </motion.div>
    </section>
  );
}
