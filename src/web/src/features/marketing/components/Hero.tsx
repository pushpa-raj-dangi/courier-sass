"use client";

import Link from "next/link";
import { motion } from "framer-motion";
import { ArrowRight } from "lucide-react";
import { getPrimaryCta, SCREENSHOTS } from "../content";
import { useRevealVariants } from "../lib/motionVariants";
import { AppScreenshotFrame } from "./AppScreenshotFrame";
import { RouteLineGraphic } from "./RouteLineGraphic";

const dashboardScreenshot = SCREENSHOTS.find((s) => s.id === "dashboard")!;

export function Hero({ isAuthenticated }: { isAuthenticated: boolean }) {
  const cta = getPrimaryCta(isAuthenticated);
  const { container, item } = useRevealVariants();

  return (
    <section className="relative w-full overflow-hidden">
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 -z-20"
        style={{
          background: "radial-gradient(ellipse 70% 55% at 78% 28%, hsl(var(--marketing-accent) / 0.14), transparent 70%)",
        }}
      />
      <RouteLineGraphic className="absolute inset-x-0 top-1/2 -z-10 h-40 w-full -translate-y-1/2 opacity-60" />
      <div
        aria-hidden="true"
        data-testid="hero-gradient-orb"
        className="pointer-events-none absolute -right-24 top-1/2 -z-10 h-[28rem] w-[28rem] -translate-y-1/2 rounded-full opacity-40 blur-3xl"
        style={{ backgroundColor: "hsl(var(--marketing-accent))" }}
      />
      <div className="relative mx-auto grid max-w-6xl gap-12 px-4 pb-24 pt-20 lg:grid-cols-[1.05fr_1fr] lg:items-center lg:gap-16">
        <motion.div
          variants={container}
          initial="hidden"
          animate="visible"
          className="flex flex-col items-center gap-6 text-center lg:items-start lg:text-left"
        >
          <motion.h1
            variants={item}
            className="font-display text-6xl font-semibold tracking-tight text-marketing-fg sm:text-7xl"
          >
            Every parcel, accounted for.
          </motion.h1>
          <motion.p variants={item} className="max-w-xl text-lg text-marketing-muted-fg">
            Scan-driven custody, hub transfers, delivery attempts, and COD settlement — one platform that always
            knows where a parcel is, who has it, and what happens next.
          </motion.p>
          <motion.div variants={item} className="flex flex-wrap items-center justify-center gap-4 lg:justify-start">
            <Link
              href={cta.href}
              className="group inline-flex items-center gap-2 rounded-lg bg-marketing-accent px-6 py-3 text-sm font-medium text-marketing-accent-fg shadow-[0_0_40px_-10px_hsl(var(--marketing-accent)/0.7)] transition-opacity hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg"
            >
              {cta.label}
              <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" />
            </Link>
            <a
              href="#walkthrough"
              className="rounded-lg border border-marketing-border px-6 py-3 text-sm font-medium text-marketing-fg transition-colors hover:bg-marketing-bg-elevated focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg"
            >
              See how it works
            </a>
          </motion.div>
        </motion.div>
        <motion.div variants={item} initial="hidden" animate="visible" className="w-full">
          <AppScreenshotFrame src={dashboardScreenshot.src} alt={dashboardScreenshot.alt} hoverTilt />
        </motion.div>
      </div>
    </section>
  );
}
