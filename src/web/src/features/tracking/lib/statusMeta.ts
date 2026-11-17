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
