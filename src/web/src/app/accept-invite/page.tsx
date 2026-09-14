import { Suspense } from "react";
import { Package } from "lucide-react";
import { AcceptInviteForm } from "@/features/auth/AcceptInviteForm";
import { MobileBrandLockup } from "@/components/auth/MobileBrandLockup";

export default function AcceptInvitePage() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-6 bg-background p-4">
      <div className="flex flex-col items-center gap-2">
        <MobileBrandLockup icon={Package} brand="Courier Platform" />
      </div>
      <Suspense>
        <AcceptInviteForm />
      </Suspense>
    </main>
  );
}
