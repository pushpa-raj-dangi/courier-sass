import { Suspense } from "react";
import { MerchantShipmentsView } from "@/features/merchant-shipments/components/MerchantShipmentsView";

// useSearchParams in the view needs a Suspense boundary for the App Router build.
export default function MerchantShipmentsPage() {
  return (
    <Suspense>
      <MerchantShipmentsView />
    </Suspense>
  );
}
