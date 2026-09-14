import Link from "next/link";
import { Package } from "lucide-react";
import { LoginForm } from "@/features/auth/LoginForm";
import { AuthShowcasePanel } from "@/components/auth/AuthShowcasePanel";
import { MobileBrandLockup } from "@/components/auth/MobileBrandLockup";
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
          <MobileBrandLockup icon={Package} brand="Courier Platform" />
        </div>
        <LoginForm />
        <p className="text-sm text-muted-foreground">
          New here?{" "}
          <Link href="/register" className="font-medium text-foreground underline-offset-4 hover:underline">
            Create an organization
          </Link>
        </p>
      </div>
    </main>
  );
}
