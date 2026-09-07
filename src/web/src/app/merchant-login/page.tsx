import { Store } from "lucide-react";
import { MerchantLoginForm } from "@/features/merchant-auth/MerchantLoginForm";
import { AuthShowcasePanel } from "@/components/auth/AuthShowcasePanel";
import type { AuthLifecycleStop } from "@/components/auth/AuthLifecycleGraphic";

const STOPS: AuthLifecycleStop[] = [
  { label: "Booked", caption: "Shipment created against your rate card.", tone: "neutral" },
  { label: "Picked up", caption: "Rider collects from your pickup point.", tone: "motion" },
  { label: "COD collected", caption: "Cash received, deposited, reconciled.", tone: "highlight" },
  { label: "Settled", caption: "Payable transferred, invoice closed.", tone: "success" },
];

export default function MerchantLoginPage() {
  return (
    <main className="grid min-h-screen lg:grid-cols-2">
      <AuthShowcasePanel
        icon={Store}
        brand="Merchant Portal"
        headline="Book, track, and get paid — in one place."
        description="Real-time shipment status and COD settlements for every order you send through the network, with no spreadsheet reconciliation."
        stops={STOPS}
      />
      <div className="flex flex-col items-center justify-center gap-6 bg-background p-4">
        <div className="flex flex-col items-center gap-2 lg:hidden">
          <span className="flex h-11 w-11 items-center justify-center rounded-lg bg-primary">
            <Store className="h-5 w-5 text-primary-foreground" strokeWidth={2} />
          </span>
          <span className="font-display text-lg font-semibold tracking-tight">Merchant Portal</span>
        </div>
        <MerchantLoginForm />
      </div>
    </main>
  );
}
