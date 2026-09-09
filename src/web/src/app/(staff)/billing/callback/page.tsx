"use client";

import { Suspense, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { useCompletePayment } from "@/features/billing/hooks/useCompletePayment";

// useSearchParams() bails out of static rendering unless wrapped in its own Suspense boundary —
// same split this codebase already uses for AcceptInviteForm/accept-invite/page.tsx — so the
// search-params-consuming logic lives in its own inner component here.
function BillingCallbackContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const completePayment = useCompletePayment();
  const [result, setResult] = useState<{ succeeded: boolean; message: string } | null>(null);

  useEffect(() => {
    const paymentId = searchParams.get("paymentId");
    if (!paymentId) {
      setResult({ succeeded: false, message: "No payment reference was returned." });
      return;
    }
    const callbackParameters: Record<string, string> = {};
    searchParams.forEach((value, key) => {
      if (key !== "paymentId") {
        callbackParameters[key] = value;
      }
    });
    completePayment.mutate(
      { paymentId, callbackParameters },
      { onSuccess: (data) => setResult(data), onError: () => setResult({ succeeded: false, message: "Payment could not be confirmed." }) }
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  if (!result) {
    return (
      <div className="flex min-h-screen flex-col items-center justify-center gap-4">
        <Spinner className="h-8 w-8" />
        <p className="text-sm text-muted-foreground">Confirming your payment…</p>
      </div>
    );
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-4 p-4 text-center">
      <p className="text-lg font-semibold">{result.succeeded ? "Payment confirmed" : "Payment not confirmed"}</p>
      <p className="text-sm text-muted-foreground">{result.message}</p>
      <Button onClick={() => router.push("/billing")}>Back to Billing</Button>
    </div>
  );
}

export default function BillingCallbackPage() {
  return (
    <Suspense
      fallback={
        <div className="flex min-h-screen flex-col items-center justify-center gap-4">
          <Spinner className="h-8 w-8" />
          <p className="text-sm text-muted-foreground">Confirming your payment…</p>
        </div>
      }
    >
      <BillingCallbackContent />
    </Suspense>
  );
}
