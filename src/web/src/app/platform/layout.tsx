import { PlatformShell } from "@/features/platform/PlatformShell";

export default function PlatformLayout({ children }: { children: React.ReactNode }) {
  return <PlatformShell>{children}</PlatformShell>;
}
