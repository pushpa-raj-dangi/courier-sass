"use client";

import { motion } from "framer-motion";
import { Package } from "lucide-react";
import { MarketingThemeToggle } from "@/features/marketing/components/MarketingThemeToggle";
import { RouteLineGraphic } from "@/features/marketing/components/RouteLineGraphic";
import { useRevealVariants } from "@/features/marketing/lib/motionVariants";
import { TrackingSearchForm } from "@/features/tracking/TrackingSearchForm";

export default function TrackPage() {
  const { container, item } = useRevealVariants();

  return (
    <div className="relative isolate min-h-screen bg-marketing-bg selection:bg-marketing-accent/40 selection:text-marketing-fg">
      <div aria-hidden="true" className="marketing-grain-overlay" />
      {/* Two-layer glow: the accent ellipse up top is the page's brand light; the warmer
          motion-tone orb lower-left reuses the same hue the shipment timeline uses for "in
          transit" — a wink at the subject matter rather than an arbitrary decorative color. */}
      <div
        aria-hidden="true"
        className="pointer-events-none fixed inset-0 -z-20"
        style={{
          background: "radial-gradient(ellipse 70% 50% at 50% 0%, hsl(var(--marketing-accent) / 0.18), transparent 70%)",
        }}
      />
      <div
        aria-hidden="true"
        className="pointer-events-none fixed -bottom-16 -left-16 -z-20 h-[26rem] w-[26rem] rounded-full opacity-30 blur-3xl"
        style={{ backgroundColor: "hsl(var(--marketing-tone-motion))" }}
      />
      <RouteLineGraphic
        className="pointer-events-none fixed inset-x-0 top-[16%] -z-10 h-72 w-full"
        strokeOpacity={0.4}
        hubOpacity={0.55}
      />

      <div className="relative z-10 flex min-h-screen flex-col">
        <div className="flex justify-end p-4">
          <MarketingThemeToggle />
        </div>
        <motion.main
          variants={container}
          initial="hidden"
          animate="visible"
          className="flex flex-1 flex-col items-center justify-center gap-10 px-4 pb-24"
        >
          <motion.div variants={item} className="flex flex-col items-center gap-4 text-center">
            <span
              className="flex h-12 w-12 items-center justify-center rounded-xl bg-marketing-accent shadow-[0_0_40px_-8px_hsl(var(--marketing-accent)/0.8)]"
              aria-hidden="true"
            >
              <Package className="h-5 w-5 text-marketing-accent-fg" strokeWidth={2} />
            </span>
            <h1 className="font-display text-5xl font-semibold tracking-tight text-marketing-fg sm:text-6xl">
              Find your parcel
            </h1>
            <p className="max-w-sm text-base text-marketing-muted-fg">
              Enter the AWB number from your receipt or SMS to see its live status.
            </p>
          </motion.div>

          <motion.div variants={item} className="relative">
            <div
              aria-hidden="true"
              className="pointer-events-none absolute -inset-6 -z-10 rounded-[2rem] opacity-60 blur-2xl"
              style={{ backgroundColor: "hsl(var(--marketing-accent) / 0.12)" }}
            />
            <TrackingSearchForm />
          </motion.div>

          <motion.p variants={item} className="text-xs text-marketing-muted-fg">
            No account needed — tracking is public for every shipment.
          </motion.p>
        </motion.main>
      </div>
    </div>
  );
}
