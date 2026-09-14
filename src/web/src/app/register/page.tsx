import Link from "next/link";
import { Package } from "lucide-react";
import { RegisterForm } from "@/features/auth/RegisterForm";
import { AuthShowcasePanel } from "@/components/auth/AuthShowcasePanel";
import { MobileBrandLockup } from "@/components/auth/MobileBrandLockup";
import type { AuthLifecycleStop } from "@/components/auth/AuthLifecycleGraphic";

const STOPS: AuthLifecycleStop[] = [
  { label: "Booked", caption: "Received at the origin branch counter.", tone: "neutral" },
  { label: "In transit", caption: "Scanned through every hub transfer.", tone: "motion" },
  { label: "Out for delivery", caption: "Assigned to a rider, attempt logged.", tone: "highlight" },
  { label: "Delivered", caption: "Proof of delivery, COD reconciled.", tone: "success" },
];

export default function RegisterPage() {
  return (
    <main className="grid min-h-screen lg:grid-cols-2">
      <AuthShowcasePanel
        icon={Package}
        brand="Courier Platform"
        headline="Set up your organization."
        description="Branches, riders, merchants, and COD — one system for your whole courier operation, ready in minutes."
        stops={STOPS}
      />
      <div className="flex flex-col items-center justify-center gap-6 bg-background p-4">
        <div className="flex flex-col items-center gap-2 lg:hidden">
          <MobileBrandLockup icon={Package} brand="Courier Platform" />
        </div>
        <RegisterForm />
        <p className="text-sm text-muted-foreground">
          Already have an account?{" "}
          <Link href="/login" className="font-medium text-foreground underline-offset-4 hover:underline">
            Sign in
          </Link>
        </p>
      </div>
    </main>
  );
}
