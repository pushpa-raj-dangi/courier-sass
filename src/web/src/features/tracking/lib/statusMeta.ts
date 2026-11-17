import {
  AlertTriangle,
  Bike,
  Building2,
  CheckCircle2,
  PackageCheck,
  Truck,
  XCircle,
  type LucideIcon,
} from "lucide-react";
import type { PublicTrackingStatus } from "@/features/tracking/types";

export type TrackingTone = "neutral" | "motion" | "success" | "alert";

const TONE_VAR: Record<TrackingTone, string> = {
  neutral: "--marketing-tone-neutral",
  motion: "--marketing-tone-motion",
  success: "--marketing-tone-success",
  alert: "--marketing-tone-alert",
};

// Mirrors marketing's TONE_COLOR (features/marketing/lib/toneColor.ts) but adds "alert" for
// DeliveryAttemptUnsuccessful, which the marketing lifecycle rail's three-tone set has no use
// for — kept local to this feature instead of widening marketing's WalkthroughTone type.
export const TRACKING_TONE_COLOR: Record<TrackingTone, string> = {
  neutral: `hsl(var(${TONE_VAR.neutral}))`,
  motion: `hsl(var(${TONE_VAR.motion}))`,
  success: `hsl(var(${TONE_VAR.success}))`,
  alert: `hsl(var(${TONE_VAR.alert}))`,
};

// Alpha-blended variant of a tone (tinted icon-chip backgrounds, glow rings) — composed with
// hsl()'s slash-alpha syntax against the same bare H/S/L token, the same technique Hero.tsx uses
// for its accent-tint gradient (`hsl(var(--marketing-accent) / 0.14)`). Appending a hex alpha
// suffix directly onto an `hsl(var(...))` string, tried earlier, produces invalid CSS that the
// browser silently drops.
export function toneAlpha(tone: TrackingTone, alpha: number): string {
  return `hsl(var(${TONE_VAR[tone]}) / ${alpha})`;
}

interface StatusMeta {
  tone: TrackingTone;
  icon: LucideIcon;
  /** One sentence, written for the recipient waiting on this parcel. */
  summary: string;
  /** Short, human headline for the hero of the result page. */
  headline: string;
}

export const STATUS_META: Record<PublicTrackingStatus, StatusMeta> = {
  Received: {
    tone: "neutral",
    icon: PackageCheck,
    summary: "Booked and waiting to be picked up.",
    headline: "Booked, waiting for pickup",
  },
  PickedUp: {
    tone: "motion",
    icon: Truck,
    summary: "Collected and on its way to the origin hub.",
    headline: "Picked up from the seller",
  },
  InTransit: {
    tone: "motion",
    icon: Truck,
    summary: "Moving between hubs toward its destination.",
    headline: "On its way",
  },
  AtDestinationBranch: {
    tone: "motion",
    icon: Building2,
    summary: "Arrived at the destination branch.",
    headline: "At your local branch",
  },
  OutForDelivery: {
    tone: "motion",
    icon: Bike,
    summary: "With a rider, on its way to the recipient.",
    headline: "Arriving today",
  },
  Delivered: { tone: "success", icon: CheckCircle2, summary: "Delivered.", headline: "Delivered" },
  DeliveryAttemptUnsuccessful: {
    headline: "We missed you",
    tone: "alert",
    icon: AlertTriangle,
    summary: "A delivery attempt was unsuccessful. A reattempt will follow.",
  },
  Cancelled: {
    tone: "neutral",
    icon: XCircle,
    summary: "This shipment was cancelled.",
    headline: "Shipment cancelled",
  },
};

// Index into marketing's four-stage WALKTHROUGH_STOPS rail (Created / In transit / Out for
// delivery / Delivered) for the progress-mode LifecycleRail on the result page. Cancelled has
// no honest position on a forward-progress rail, so it renders no rail at all (see page.tsx).
export function railStageIndex(status: PublicTrackingStatus): number | null {
  switch (status) {
    case "Received":
      return 0;
    case "PickedUp":
    case "InTransit":
    case "AtDestinationBranch":
      return 1;
    case "OutForDelivery":
    case "DeliveryAttemptUnsuccessful":
      return 2;
    case "Delivered":
      return 3;
    case "Cancelled":
      return null;
  }
}
