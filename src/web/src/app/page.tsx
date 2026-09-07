import { getValidAccessToken } from "@/lib/auth/session";
import { MarketingNav } from "@/features/marketing/components/MarketingNav";
import { Hero } from "@/features/marketing/components/Hero";
import { FeatureGrid } from "@/features/marketing/components/FeatureGrid";
import { HowItWorks } from "@/features/marketing/components/HowItWorks";
import { ProductWalkthrough } from "@/features/marketing/components/ProductWalkthrough";
import { Credibility } from "@/features/marketing/components/Credibility";
import { FinalCta } from "@/features/marketing/components/FinalCta";
import { MarketingFooter } from "@/features/marketing/components/MarketingFooter";

export default async function RootPage() {
  const accessToken = await getValidAccessToken();
  const isAuthenticated = Boolean(accessToken);

  return (
    <div className="min-h-screen bg-marketing-bg selection:bg-marketing-accent/40 selection:text-marketing-fg">
      <MarketingNav isAuthenticated={isAuthenticated} />
      <main>
        <Hero isAuthenticated={isAuthenticated} />
        <HowItWorks />
        <ProductWalkthrough />
        <FeatureGrid />
        <Credibility />
        <FinalCta isAuthenticated={isAuthenticated} />
      </main>
      <MarketingFooter />
    </div>
  );
}
