"use client";

import Link from "next/link";
import { motion } from "framer-motion";
import { ArrowRight } from "lucide-react";
import { getPrimaryCta } from "../content";
import { useRevealVariants } from "../lib/motionVariants";

export function Hero({ isAuthenticated }: { isAuthenticated: boolean }) {
  const cta = getPrimaryCta(isAuthenticated);
  const { container, item } = useRevealVariants();

  return (
    <section className="mx-auto flex max-w-6xl flex-col items-center gap-8 px-4 pb-24 pt-20 text-center">
      <motion.div variants={container} initial="hidden" animate="visible" className="flex flex-col items-center gap-6">
        <motion.h1
          variants={item}
          className="font-display text-5xl font-semibold tracking-tight text-marketing-fg sm:text-6xl lg:text-7xl"
        >
          Every parcel, <span className="text-marketing-accent">accounted for.</span>
        </motion.h1>
        <motion.p variants={item} className="max-w-2xl text-lg text-marketing-muted-fg">
          Scan-driven custody, hub transfers, delivery attempts, and COD settlement — one platform that always
          knows where a parcel is, who has it, and what happens next.
        </motion.p>
        <motion.div variants={item} className="flex flex-wrap items-center justify-center gap-4">
          <Link
            href={cta.href}
            className="group inline-flex items-center gap-2 rounded-lg bg-marketing-accent px-6 py-3 text-sm font-medium text-marketing-accent-fg transition-opacity hover:opacity-90"
          >
            {cta.label}
            <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" />
          </Link>
          <a
            href="#walkthrough"
            className="rounded-lg border border-marketing-border px-6 py-3 text-sm font-medium text-marketing-fg transition-colors hover:bg-marketing-bg-elevated"
          >
            See how it works
          </a>
        </motion.div>
        <motion.div
          variants={item}
          data-testid="hero-status-preview"
          className="mt-4 flex items-center gap-3 rounded-lg border border-marketing-border bg-marketing-bg-elevated px-4 py-3"
        >
          <span className="h-2 w-2 rounded-full bg-marketing-accent" aria-hidden="true" />
          <span className="font-mono text-xs text-marketing-muted-fg">AWB NP2026-000000</span>
          <span className="text-xs font-medium text-marketing-fg">Created</span>
        </motion.div>
      </motion.div>
    </section>
  );
}
