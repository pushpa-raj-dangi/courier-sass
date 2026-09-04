import { RiderShell } from "@/components/layout/RiderShell";
import { RiderServiceWorkerRegistration } from "@/components/layout/RiderServiceWorkerRegistration";
import { RiderSheets } from "@/features/rider-app/components/RiderSheets";

export const metadata = {
  manifest: "/rider-manifest.webmanifest",
};

export default function RiderLayout({ children }: { children: React.ReactNode }) {
  return (
    <RiderShell>
      <RiderServiceWorkerRegistration />
      {children}
      <RiderSheets />
    </RiderShell>
  );
}
