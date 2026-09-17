import { hasSession } from "@/lib/auth/session";
import { MarketingNav } from "@/features/marketing/components/MarketingNav";
import { PricingPlans } from "@/features/marketing/components/PricingPlans";
import { PricingFaq } from "@/features/marketing/components/PricingFaq";
import { FinalCta } from "@/features/marketing/components/FinalCta";
import { MarketingFooter } from "@/features/marketing/components/MarketingFooter";

export default async function PricingPage() {
  const isAuthenticated = hasSession();

  return (
    <div className="min-h-screen bg-marketing-bg selection:bg-marketing-accent/40 selection:text-marketing-fg">
      <div aria-hidden="true" className="marketing-grain-overlay" data-testid="marketing-grain-overlay" />
      <div className="relative z-10">
        <MarketingNav isAuthenticated={isAuthenticated} />
        <main>
          <PricingPlans isAuthenticated={isAuthenticated} />
          <PricingFaq />
          <FinalCta isAuthenticated={isAuthenticated} />
        </main>
        <MarketingFooter />
      </div>
    </div>
  );
}
