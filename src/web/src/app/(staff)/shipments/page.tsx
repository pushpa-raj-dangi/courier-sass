import { Suspense } from "react";
import { ShipmentsView } from "@/features/shipments/components/ShipmentsView";

// useSearchParams in the view needs a Suspense boundary for the App Router build.
export default function ShipmentsPage() {
  return (
    <Suspense>
      <ShipmentsView />
    </Suspense>
  );
}
