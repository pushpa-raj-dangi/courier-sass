"use client";

import Link from "next/link";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useTrialStatus } from "@/features/billing/hooks/useTrialStatus";

function daysLeft(trialEndsAt: string): number {
  const ms = new Date(trialEndsAt).getTime() - Date.now();
  return Math.max(0, Math.ceil(ms / (1000 * 60 * 60 * 24)));
}

// Amber/destructive inline colors here are deliberate and independent of the app's theme
// tokens, same reasoning as ImpersonationBanner — a paywall banner needs to read as an
// unmistakable warning in both light and dark mode.
export function TrialStatusBanner() {
  const canManageBilling = usePermission("org.billing.manage");
  const { data: status } = useTrialStatus();

  if (!status || status.status === "Active" || status.status === "PastDue" || status.status === "Cancelled") {
    return null;
  }

  const expired = status.status === "TrialExpired";
  const bannerClass = expired
    ? "flex items-center justify-between gap-3 bg-destructive px-4 py-2 text-sm font-medium text-destructive-foreground"
    : "flex items-center justify-between gap-3 bg-amber-500 px-4 py-2 text-sm font-medium text-amber-950";

  const days = status.trialEndsAt ? daysLeft(status.trialEndsAt) : null;
  const message = expired
    ? "Your trial has ended — some actions are blocked until a plan is chosen."
    : days !== null
      ? `You're on a free trial — ${days} day${days === 1 ? "" : "s"} left.`
      : "You're on a free trial.";

  return (
    <div className={bannerClass}>
      <span>{message}</span>
      {canManageBilling ? (
        <Link href="/billing" className="shrink-0 underline underline-offset-2">
          Choose a plan
        </Link>
      ) : (
        <span className="shrink-0 text-xs opacity-80">Ask your organization owner to choose a plan.</span>
      )}
    </div>
  );
}
