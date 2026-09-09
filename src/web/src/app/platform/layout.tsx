import { PlatformNav } from "@/features/platform/PlatformNav";

export default function PlatformLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen bg-background">
      <PlatformNav />
      {children}
    </div>
  );
}
