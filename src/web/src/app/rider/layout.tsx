import { RiderShell } from "@/components/layout/RiderShell";
import { RiderServiceWorkerRegistration } from "@/components/layout/RiderServiceWorkerRegistration";
import { RiderLocationReporting } from "@/components/layout/RiderLocationReporting";
import { RiderSheets } from "@/features/rider-app/components/RiderSheets";

export const metadata = {
  manifest: "/rider-manifest.webmanifest",
};

export default function RiderLayout({ children }: { children: React.ReactNode }) {
  return (
    <RiderShell>
      <RiderServiceWorkerRegistration />
      <RiderLocationReporting />
      {children}
      <RiderSheets />
    </RiderShell>
  );
}
