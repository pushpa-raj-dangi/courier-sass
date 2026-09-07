"use client";

import { useEffect, useState } from "react";
import { motion } from "framer-motion";
import { WALKTHROUGH_STOPS, type WalkthroughTone } from "../content";
import { useRevealVariants } from "../lib/motionVariants";

// Literal HSL values, not the shared --status-* tokens: these are the app's own
// dark-mode status variants (globals.css .dark block), reused directly because this
// section always sits on the fixed --marketing-bg (near-black) regardless of visitor
// theme — the same reasoning AuthLifecycleGraphic documents for the auth panel.
// Verified against --marketing-bg (228 20% 6%): created 8.14:1, in-transit/out-for-
// delivery 9.38:1, delivered 7.53:1 — all clear both the 3:1 non-text and 4.5:1 text
// floors. See the plan's "Verified Design Tokens" table.
const TONE_COLOR: Record<WalkthroughTone, string> = {
  neutral: "hsl(234.3 13.2% 68.8%)",
  motion: "hsl(38.0 78.9% 57.3%)",
  success: "hsl(169.7 47.9% 47.5%)",
};

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

  const activeStop = WALKTHROUGH_STOPS[activeIndex];

  return (
    <section id="walkthrough" className="mx-auto max-w-3xl px-4 py-24">
      <h2 className="mb-8 text-center font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">
        One parcel, every step accounted for
      </h2>
      <div data-testid="walkthrough-frame" className="rounded-xl border border-marketing-border bg-marketing-bg-elevated p-8">
        <motion.div
          key={activeStop.label}
          initial={{ opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.4 }}
          className="flex flex-col items-center gap-4 text-center"
        >
          <span
            className="h-3 w-3 rounded-full"
            style={{ backgroundColor: TONE_COLOR[activeStop.tone] }}
            aria-hidden="true"
          />
          <p className="font-mono text-xs text-marketing-muted-fg">AWB NP2026-000000</p>
          <p className="font-display text-xl font-semibold text-marketing-fg">{activeStop.label}</p>
          <p className="text-sm text-marketing-muted-fg">{activeStop.caption}</p>
        </motion.div>
        <div className="mt-6 flex items-center justify-center gap-2">
          {WALKTHROUGH_STOPS.map((stop, index) => (
            <span
              key={stop.label}
              data-testid="walkthrough-dot"
              className="h-1.5 w-1.5 rounded-full transition-opacity"
              style={{ backgroundColor: TONE_COLOR[stop.tone], opacity: index === activeIndex ? 1 : 0.3 }}
              aria-hidden="true"
            />
          ))}
        </div>
      </div>
    </section>
  );
}
