"use client";

import { useEffect, useState } from "react";
import { SCREENSHOTS, WALKTHROUGH_STOPS } from "../content";
import { useRevealVariants } from "../lib/motionVariants";
import { AppScreenshotFrame } from "./AppScreenshotFrame";
import { LifecycleRail } from "./LifecycleRail";

const CYCLE_MS = 2800;
const shipmentDetailScreenshot = SCREENSHOTS.find((s) => s.id === "shipment-detail")!;

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

  return (
    <section id="walkthrough" className="mx-auto max-w-3xl px-4 py-24">
      <h2 className="mb-8 text-center font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">
        One parcel, every step accounted for
      </h2>
      <div className="flex flex-col gap-6">
        <AppScreenshotFrame src={shipmentDetailScreenshot.src} alt={shipmentDetailScreenshot.alt} />
        <div data-testid="walkthrough-rail">
          <LifecycleRail
            stops={WALKTHROUGH_STOPS.map((stop) => ({ tone: stop.tone, label: stop.label }))}
            activeIndex={activeIndex}
            showLabels
          />
        </div>
      </div>
    </section>
  );
}
