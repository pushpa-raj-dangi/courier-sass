import { ShieldCheck } from "lucide-react";
import { PlatformLoginForm } from "@/features/platform-auth/PlatformLoginForm";
import { AuthShowcasePanel } from "@/components/auth/AuthShowcasePanel";
import type { AuthLifecycleStop } from "@/components/auth/AuthLifecycleGraphic";

const STOPS: AuthLifecycleStop[] = [
  { label: "Organization provisioned", caption: "A new courier company is onboarded to the platform.", tone: "neutral" },
  { label: "Plan assigned", caption: "Branch, user, and API limits set by subscription tier.", tone: "motion" },
  { label: "Invoice generated", caption: "Billing period closes, an invoice is raised automatically.", tone: "highlight" },
  { label: "Payment settled", caption: "Subscription renewed, organization stays in good standing.", tone: "success" },
];

export default function PlatformLoginPage() {
  return (
    <main className="grid min-h-screen lg:grid-cols-2">
      <AuthShowcasePanel
        icon={ShieldCheck}
        brand="Platform Administration"
        headline="Every organization, in good standing."
        description="Provision courier companies, manage subscription plans, and oversee billing across the entire platform — one console, every tenant."
        stops={STOPS}
      />
      <div className="flex flex-col items-center justify-center gap-6 bg-background p-4">
        <div className="flex flex-col items-center gap-2 lg:hidden">
          <span className="flex h-11 w-11 items-center justify-center rounded-lg bg-primary">
            <ShieldCheck className="h-5 w-5 text-primary-foreground" strokeWidth={2} />
          </span>
          <span className="font-display text-lg font-semibold tracking-tight">Platform Administration</span>
        </div>
        <PlatformLoginForm />
      </div>
    </main>
  );
}
