import { Package } from "lucide-react";
import { LoginForm } from "@/features/auth/LoginForm";
import { AuthShowcasePanel } from "@/components/auth/AuthShowcasePanel";
import type { AuthLifecycleStop } from "@/components/auth/AuthLifecycleGraphic";

const STOPS: AuthLifecycleStop[] = [
  { label: "Booked", caption: "Received at the origin branch counter.", tone: "neutral" },
  { label: "In transit", caption: "Scanned through every hub transfer.", tone: "motion" },
  { label: "Out for delivery", caption: "Assigned to a rider, attempt logged.", tone: "highlight" },
  { label: "Delivered", caption: "Proof of delivery, COD reconciled.", tone: "success" },
];

export default function LoginPage() {
  return (
    <main className="grid min-h-screen lg:grid-cols-2">
      <AuthShowcasePanel
        icon={Package}
        brand="Courier Platform"
        headline="Every parcel, accounted for."
        description="Scan-driven custody, hub transfers, delivery attempts and COD settlement — one system that always knows where a parcel is and who has it."
        stops={STOPS}
      />
      <div className="flex flex-col items-center justify-center gap-6 bg-background p-4">
        <div className="flex flex-col items-center gap-2 lg:hidden">
          <span className="flex h-11 w-11 items-center justify-center rounded-lg bg-primary">
            <Package className="h-5 w-5 text-primary-foreground" strokeWidth={2} />
          </span>
          <span className="font-display text-lg font-semibold tracking-tight">Courier Platform</span>
        </div>
        <LoginForm />
      </div>
    </main>
  );
}
