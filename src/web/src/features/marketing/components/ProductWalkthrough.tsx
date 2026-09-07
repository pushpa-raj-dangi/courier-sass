"use client";

import { useEffect, useState } from "react";
import { motion } from "framer-motion";
import { WALKTHROUGH_STOPS } from "../content";
import { useRevealVariants } from "../lib/motionVariants";
import { TONE_COLOR } from "../lib/toneColor";
import { ProductMockupPanel } from "./ProductMockupPanel";

const CYCLE_MS = 2800;

export function ProductWalkthrough() {
  const { prefersReducedMotion } = useRevealVariants();
  const [activeIndex, setActiveIndex] = useState(0);

  useEffect(() => {
    if (prefersReducedMotion) return;
    const id = window.setInterval(() => {
      setActiveIndex((current) => (current + 1) % WALKTHROUGH_STOPS.length);
    }, CYCLE_MS);
    return () => window.clearInterval(id);
  }, [prefersReducedMotion]);

  if (prefersReducedMotion) {
    return (
      <section id="walkthrough" className="mx-auto max-w-3xl px-4 py-24">
        <h2 className="mb-8 text-center font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">
          One parcel, every step accounted for
        </h2>
        <ul className="flex flex-col gap-4 rounded-xl border border-marketing-border bg-marketing-bg-elevated p-6">
          {WALKTHROUGH_STOPS.map((stop) => (
            <li key={stop.label} className="flex items-center gap-3">
              <span
                className="h-2 w-2 shrink-0 rounded-full"
                style={{ backgroundColor: TONE_COLOR[stop.tone] }}
                aria-hidden="true"
              />
              <span className="text-sm font-medium text-marketing-fg">{stop.label}</span>
              <span className="text-sm text-marketing-muted-fg">{stop.caption}</span>
            </li>
          ))}
        </ul>
      </section>
    );
  }

  return (
    <section id="walkthrough" className="mx-auto max-w-3xl px-4 py-24">
      <h2 className="mb-8 text-center font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">
        One parcel, every step accounted for
      </h2>
      <motion.div
        key={WALKTHROUGH_STOPS[activeIndex].label}
        data-testid="walkthrough-frame"
        initial={{ opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.4 }}
      >
        <ProductMockupPanel stops={WALKTHROUGH_STOPS} activeIndex={activeIndex} size="full" />
      </motion.div>
    </section>
  );
}
