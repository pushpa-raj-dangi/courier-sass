import { Suspense } from "react";
import { ScanStation } from "@/features/scanning/components/ScanStation";

export default function ScanPage() {
  return (
    <Suspense>
      <ScanStation />
    </Suspense>
  );
}
