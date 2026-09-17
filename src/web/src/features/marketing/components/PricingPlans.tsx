"use client";

import Link from "next/link";
import { motion } from "framer-motion";
import { Check } from "lucide-react";
import { PRICING_PLANS, getPrimaryCta, type PricingPlan } from "../content";
import { useRevealVariants } from "../lib/motionVariants";

function formatCount(value: number | null, unit: string): string {
  return value === null ? `Unlimited ${unit}` : `${value.toLocaleString("en-US")} ${unit}`;
}

function planLimits(plan: PricingPlan): string[] {
  return [
    formatCount(plan.maxBranches, plan.maxBranches === 1 ? "branch" : "branches"),
    formatCount(plan.maxStaffUsers, "staff accounts"),
    formatCount(plan.maxMonthlyShipments, "shipments / month"),
    "7-day free trial",
  ];
}

function PlanCard({ plan, isAuthenticated }: { plan: PricingPlan; isAuthenticated: boolean }) {
  const cta = getPrimaryCta(isAuthenticated);

  return (
    <div
      data-testid="pricing-card"
      className={
        plan.isDefault
          ? "relative flex flex-col gap-6 rounded-xl border border-marketing-accent bg-marketing-bg-elevated p-6 shadow-[0_0_50px_-15px_hsl(var(--marketing-accent)/0.7)]"
          : "flex flex-col gap-6 rounded-xl border border-marketing-border bg-marketing-bg-elevated p-6 transition-shadow hover:shadow-[0_0_40px_-15px_hsl(var(--marketing-accent)/0.4)]"
      }
    >
      {plan.isDefault && (
        <span className="absolute -top-3 left-6 rounded-full bg-marketing-accent px-3 py-1 text-xs font-medium text-marketing-accent-fg">
          Most teams start here
        </span>
      )}
      <div className="flex flex-col gap-1.5">
        <h2 className="font-display text-lg font-semibold text-marketing-fg">{plan.name}</h2>
        <p className="text-sm text-marketing-muted-fg">{plan.tagline}</p>
      </div>
      <div className="flex flex-col gap-1">
        {plan.monthlyPriceNpr === null ? (
          <span className="font-display text-3xl font-semibold text-marketing-fg">Custom</span>
        ) : (
          <>
            <span className="whitespace-nowrap font-display text-3xl font-semibold text-marketing-fg">
              NPR {plan.monthlyPriceNpr.toLocaleString("en-US")}
            </span>
            <span className="text-sm text-marketing-muted-fg">per month</span>
          </>
        )}
      </div>
      <ul className="flex flex-col gap-2.5">
        {planLimits(plan).map((limit) => (
          <li key={limit} className="flex items-start gap-2.5 text-sm text-marketing-fg">
            <Check className="mt-0.5 h-4 w-4 shrink-0 text-marketing-accent" strokeWidth={2} aria-hidden="true" />
            {limit}
          </li>
        ))}
      </ul>
      <Link
        href={cta.href}
        aria-label={`${cta.label} — ${plan.name} plan`}
        className={
          plan.isDefault
            ? "mt-auto rounded-lg bg-marketing-accent px-4 py-2.5 text-center text-sm font-medium text-marketing-accent-fg transition-opacity hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg"
            : "mt-auto rounded-lg border border-marketing-border px-4 py-2.5 text-center text-sm font-medium text-marketing-fg transition-colors hover:bg-marketing-bg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg"
        }
      >
        {cta.label}
      </Link>
    </div>
  );
}

export function PricingPlans({ isAuthenticated }: { isAuthenticated: boolean }) {
  const { container, item } = useRevealVariants();

  return (
    <section id="plans" className="mx-auto max-w-6xl px-4 pb-24 pt-20">
      <div className="mb-12 flex flex-col items-center gap-3 text-center">
        <h1 className="font-display text-4xl font-semibold tracking-tight text-marketing-fg sm:text-5xl">
          Priced by what you run, not what you promise
        </h1>
        <p className="max-w-2xl text-marketing-muted-fg">
          Every plan gets the full platform — branches, riders, COD, NDR, RTO. The tier is just how many branches,
          staff, and shipments it&apos;s sized for. NPR only, billed monthly, no hidden fees.
        </p>
      </div>
      <motion.div
        variants={container}
        initial="hidden"
        whileInView="visible"
        viewport={{ once: true, amount: 0.15 }}
        className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-4"
      >
        {PRICING_PLANS.map((plan) => (
          <motion.div key={plan.slug} variants={item}>
            <PlanCard plan={plan} isAuthenticated={isAuthenticated} />
          </motion.div>
        ))}
      </motion.div>
    </section>
  );
}
