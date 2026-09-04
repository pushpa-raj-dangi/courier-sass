import { RiderShell } from "@/components/layout/RiderShell";
import { RiderServiceWorkerRegistration } from "@/components/layout/RiderServiceWorkerRegistration";

export const metadata = {
  manifest: "/rider-manifest.webmanifest",
};

export default function RiderLayout({ children }: { children: React.ReactNode }) {
  return (
    <RiderShell>
      <RiderServiceWorkerRegistration />
      {children}
    </RiderShell>
  );
}
