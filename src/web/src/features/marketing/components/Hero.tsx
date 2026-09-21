"use client";

import Link from "next/link";
import { motion } from "framer-motion";
import { ArrowRight } from "lucide-react";
import { MANIFEST_SAMPLE, getPrimaryCta, SCREENSHOTS } from "../content";
import { codBreakdown, formatNpr } from "../lib/diagramMath";
import { useRevealVariants } from "../lib/motionVariants";
import { AppScreenshotFrame } from "./AppScreenshotFrame";
import { CountUp } from "./CountUp";
import { MarketingGrid } from "./MarketingGrid";
import { MockCard, ScanBar } from "./MockCard";
import { MonoLabel } from "./MonoLabel";

const dashboardScreenshot = SCREENSHOTS.find((s) => s.id === "dashboard")!;
const cod = codBreakdown(2500, 150);
const manifestScanned = MANIFEST_SAMPLE.expected - MANIFEST_SAMPLE.missingIndices.length;

export function Hero({ isAuthenticated }: { isAuthenticated: boolean }) {
  const cta = getPrimaryCta(isAuthenticated);
  const { container, item } = useRevealVariants();

  return (
    <section className="relative w-full overflow-hidden">
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 -z-20"
        style={{
          background:
            "radial-gradient(ellipse 60% 50% at 80% 20%, hsl(var(--marketing-accent) / 0.12), transparent 70%)",
        }}
      />
      <MarketingGrid />
      <motion.div
        variants={container}
        initial="hidden"
        animate="visible"
        className="relative mx-auto max-w-6xl px-4 pb-24 pt-16 md:pb-32 md:pt-24"
      >
        <motion.div variants={item} className="flex flex-wrap items-center gap-x-6 gap-y-2">
          <MonoLabel className="text-marketing-accent">Nepal domestic courier operations</MonoLabel>
          <MonoLabel>AWB NP2026-000000 · KTM → PKR · In transit</MonoLabel>
        </motion.div>

        <motion.h1
          variants={item}
          className="mt-6 max-w-5xl font-display text-[clamp(2.75rem,8vw,6.5rem)] font-semibold leading-[0.95] tracking-tight text-marketing-fg"
        >
          Every parcel, accounted for.
        </motion.h1>

        <div className="mt-8 grid gap-8 md:grid-cols-[1.2fr_1fr] md:items-end">
          <motion.p variants={item} className="max-w-xl text-lg text-marketing-muted-fg">
            Scan-driven custody, hub transfers, delivery attempts, and COD settlement — one platform that always
            knows where a parcel is, who has it, and what happens next.
          </motion.p>
          <motion.div variants={item} className="flex flex-wrap items-center gap-3 md:justify-end">
            <Link
              href={cta.href}
              className="group inline-flex items-center gap-2 rounded-md bg-marketing-accent px-6 py-3 text-sm font-medium text-marketing-accent-fg shadow-[4px_4px_0_0_hsl(var(--marketing-fg)/0.18)] transition-transform hover:-translate-y-0.5 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg"
            >
              {cta.label}
              <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" />
            </Link>
            <a
              href="#custody"
              className="rounded-md border border-marketing-border px-6 py-3 text-sm font-medium text-marketing-fg transition-colors hover:bg-marketing-bg-elevated focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg"
            >
              See how it works
            </a>
          </motion.div>
        </div>

        <motion.div variants={item} className="relative mt-14 md:mt-20">
          <div className="md:ml-16">
            <AppScreenshotFrame src={dashboardScreenshot.src} alt={dashboardScreenshot.alt} hoverTilt priority />
          </div>
          <div className="mt-6 grid gap-4 sm:grid-cols-3 md:contents">
            <MockCard label="Scan received" className="md:absolute md:-left-2 md:top-[52%] md:w-64">
              <p className="font-mono text-sm text-marketing-fg">NP2026-000000</p>
              <p className="mt-1 text-sm text-marketing-muted-fg">Received · Kathmandu Hub</p>
              <div className="mt-3">
                <ScanBar />
              </div>
            </MockCard>
            <MockCard label="COD ledger" className="md:absolute md:-bottom-10 md:right-4 md:w-72">
              <dl className="flex flex-col gap-1 text-sm">
                <div className="flex justify-between gap-4">
                  <dt className="text-marketing-muted-fg">Collected</dt>
                  <dd className="font-mono text-marketing-fg">
                    NPR <CountUp value={cod.collected} />
                  </dd>
                </div>
                <div className="flex justify-between gap-4">
                  <dt className="text-marketing-muted-fg">Courier fee</dt>
                  <dd className="font-mono text-marketing-fg">− {formatNpr(cod.fee)}</dd>
                </div>
                <div className="flex justify-between gap-4 border-t border-marketing-border pt-1">
                  <dt className="font-medium text-marketing-fg">Merchant payable</dt>
                  <dd className="font-mono font-medium text-marketing-fg">{formatNpr(cod.payable)}</dd>
                </div>
              </dl>
            </MockCard>
            <MockCard label="Manifest" className="md:absolute md:-top-8 md:right-8 md:w-60">
              <p className="font-mono text-xs text-marketing-fg">{MANIFEST_SAMPLE.code}</p>
              <p className="mt-1 text-sm text-marketing-muted-fg">
                <CountUp value={manifestScanned} /> / {MANIFEST_SAMPLE.expected} scanned ·{" "}
                {MANIFEST_SAMPLE.missingIndices.length} missing
              </p>
            </MockCard>
          </div>
        </motion.div>
      </motion.div>
    </section>
  );
}
