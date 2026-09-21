import { hasSession } from "@/lib/auth/session";
import { MarketingNav } from "@/features/marketing/components/MarketingNav";
import { Hero } from "@/features/marketing/components/Hero";
import { PromiseStrip } from "@/features/marketing/components/PromiseStrip";
import { ThreadLayout } from "@/features/marketing/components/ThreadLayout";
import { CustodySection } from "@/features/marketing/components/CustodySection";
import { ScanSection } from "@/features/marketing/components/ScanSection";
import { CodSection } from "@/features/marketing/components/CodSection";
import { ExceptionsSection } from "@/features/marketing/components/ExceptionsSection";
import { RolesSection } from "@/features/marketing/components/RolesSection";
import { ScreenShowcase } from "@/features/marketing/components/ScreenShowcase";
import { FinalCta } from "@/features/marketing/components/FinalCta";
import { MarketingFooter } from "@/features/marketing/components/MarketingFooter";

export default async function RootPage() {
  const isAuthenticated = hasSession();

  return (
    <div className="min-h-screen bg-marketing-bg selection:bg-marketing-accent/40 selection:text-marketing-fg">
      <div aria-hidden="true" className="marketing-grain-overlay" data-testid="marketing-grain-overlay" />
      <div className="relative z-10">
        <MarketingNav isAuthenticated={isAuthenticated} />
        <main>
          <Hero isAuthenticated={isAuthenticated} />
          <PromiseStrip />
          <ThreadLayout>
            <CustodySection />
            <ScanSection />
            <CodSection />
            <ExceptionsSection />
            <RolesSection />
            <ScreenShowcase />
          </ThreadLayout>
          <FinalCta isAuthenticated={isAuthenticated} />
        </main>
        <MarketingFooter />
      </div>
    </div>
  );
}
