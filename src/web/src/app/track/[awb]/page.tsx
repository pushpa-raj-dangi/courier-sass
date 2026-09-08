"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { AlertCircle, ArrowLeft, Package, PackageSearch, type LucideIcon } from "lucide-react";
import { Skeleton } from "@/components/ui/skeleton";
import { resolveStatusConfig } from "@/components/status-badge/statusConfig";
import { LifecycleRail } from "@/features/marketing/components/LifecycleRail";
import { WALKTHROUGH_STOPS } from "@/features/marketing/content";
import { MarketingThemeToggle } from "@/features/marketing/components/MarketingThemeToggle";
import { TrackingTimeline } from "@/features/tracking/components/TrackingTimeline";
import { STATUS_META, TRACKING_TONE_COLOR, railStageIndex, toneAlpha } from "@/features/tracking/lib/statusMeta";
import {
  useTrackShipment,
  TrackingNotFoundError,
  TrackingRateLimitedError,
} from "@/features/tracking/hooks/useTrackShipment";

const RAIL_STOPS = WALKTHROUGH_STOPS.map((stop) => ({ tone: stop.tone, label: stop.label }));

function EmptyState({ icon: Icon, message }: { icon: LucideIcon; message: string }) {
  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed border-marketing-border p-12 text-center">
      <Icon className="h-8 w-8 text-marketing-muted-fg" strokeWidth={1.5} />
      <p className="text-sm text-marketing-muted-fg">{message}</p>
    </div>
  );
}

export default function TrackingResultPage() {
  const params = useParams<{ awb: string }>();
  const awb = params.awb;
  const { data, isLoading, isError, error } = useTrackShipment(awb);

  const meta = data ? STATUS_META[data.currentStatus] : undefined;
  const Icon = meta?.icon ?? PackageSearch;
  const tone = meta ? TRACKING_TONE_COLOR[meta.tone] : undefined;
  const railIndex = data ? railStageIndex(data.currentStatus) : null;

  return (
    <div className="min-h-screen bg-marketing-bg selection:bg-marketing-accent/40 selection:text-marketing-fg">
      <div aria-hidden="true" className="marketing-grain-overlay" />
      <div className="relative z-10">
        <header className="border-b border-marketing-border/60">
          <div className="mx-auto flex h-16 max-w-2xl items-center justify-between px-4">
            <Link href="/" className="flex items-center gap-2 rounded-md">
              <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-marketing-accent" aria-hidden="true">
                <Package className="h-4 w-4 text-marketing-accent-fg" strokeWidth={2} />
              </span>
              <span className="font-display text-base font-semibold tracking-tight text-marketing-fg">
                Courier Platform
              </span>
            </Link>
            <div className="flex items-center gap-1">
              <Link
                href="/track"
                aria-label="Track another parcel"
                className="flex h-11 items-center gap-1.5 rounded-md px-3 text-sm text-marketing-muted-fg transition-colors hover:text-marketing-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent"
              >
                <ArrowLeft className="h-3.5 w-3.5 shrink-0" strokeWidth={2} aria-hidden="true" />
                <span className="hidden sm:inline">Track another parcel</span>
              </Link>
              <MarketingThemeToggle />
            </div>
          </div>
        </header>

        <main className="mx-auto max-w-2xl px-4 py-12">
          <p className="font-mono text-xs tracking-wide text-marketing-muted-fg">AWB {awb}</p>

          {isLoading && (
            <div className="mt-4 space-y-3">
              <Skeleton className="h-10 w-48 bg-marketing-bg-elevated" />
              <Skeleton className="h-4 w-64 bg-marketing-bg-elevated" />
              <Skeleton className="h-24 w-full bg-marketing-bg-elevated" />
              <Skeleton className="h-24 w-full bg-marketing-bg-elevated" />
            </div>
          )}

          {isError && error instanceof TrackingNotFoundError && (
            <div className="mt-4">
              <EmptyState icon={PackageSearch} message="No shipment found for this AWB." />
            </div>
          )}

          {isError && error instanceof TrackingRateLimitedError && (
            <div className="mt-4">
              <EmptyState
                icon={AlertCircle}
                message={`Too many lookups. Try again in ${error.retryAfterSeconds} seconds.`}
              />
            </div>
          )}

          {isError && !(error instanceof TrackingNotFoundError) && !(error instanceof TrackingRateLimitedError) && (
            <div className="mt-4">
              <EmptyState icon={AlertCircle} message="Something went wrong. Please try again." />
            </div>
          )}

          {data && meta && (
            <div className="mt-4">
              <div className="flex items-start gap-4">
                <span
                  className="flex h-14 w-14 shrink-0 items-center justify-center rounded-full"
                  style={{ backgroundColor: toneAlpha(meta.tone, 0.1), boxShadow: `0 0 40px -12px ${tone}` }}
                  aria-hidden="true"
                >
                  <Icon className="h-6 w-6" strokeWidth={2} style={{ color: tone }} />
                </span>
                <div>
                  <h1 className="font-display text-3xl font-semibold tracking-tight text-marketing-fg sm:text-4xl">
                    {resolveStatusConfig(data.currentStatus, "Public").label}
                  </h1>
                  <p className="mt-1 text-sm text-marketing-muted-fg">{meta.summary}</p>
                </div>
              </div>

              {railIndex !== null && (
                <div className="mt-8">
                  <LifecycleRail stops={RAIL_STOPS} activeIndex={railIndex} showLabels dotSize="lg" />
                </div>
              )}

              <div className="mt-12">
                <h2 className="mb-6 font-display text-base font-semibold text-marketing-fg">Shipment journey</h2>
                <TrackingTimeline events={data.events} />
              </div>
            </div>
          )}
        </main>
      </div>
    </div>
  );
}
