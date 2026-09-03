import { Store } from "lucide-react";
import { MerchantLoginForm } from "@/features/merchant-auth/MerchantLoginForm";

export default function MerchantLoginPage() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-6 bg-background p-4">
      <div className="flex flex-col items-center gap-2">
        <span className="flex h-11 w-11 items-center justify-center rounded-lg bg-primary">
          <Store className="h-5 w-5 text-primary-foreground" strokeWidth={2} />
        </span>
        <span className="font-display text-lg font-semibold tracking-tight">Merchant Portal</span>
      </div>
      <MerchantLoginForm />
    </main>
  );
}
