"use client";

import Link from "next/link";
import { motion } from "framer-motion";
import { getPrimaryCta } from "../content";
import { useRevealVariants } from "../lib/motionVariants";

export function FinalCta({ isAuthenticated }: { isAuthenticated: boolean }) {
  const cta = getPrimaryCta(isAuthenticated);
  const { item } = useRevealVariants();

  return (
    <section className="mx-auto max-w-3xl px-4 py-24 text-center">
      <motion.div
        variants={item}
        initial="hidden"
        whileInView="visible"
        viewport={{ once: true, amount: 0.4 }}
        className="flex flex-col items-center gap-6"
      >
        <h2 className="font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">
          Run your courier operation on one system
        </h2>
        <p className="max-w-xl text-marketing-muted-fg">
          Branches, riders, merchants, and COD — all traceable, from booking to settlement.
        </p>
        <Link
          href={cta.href}
          className="rounded-lg bg-marketing-accent px-6 py-3 text-sm font-medium text-marketing-accent-fg transition-opacity hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg"
        >
          {cta.label}
        </Link>
      </motion.div>
    </section>
  );
}
