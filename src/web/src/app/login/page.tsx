import Link from "next/link";
import { Package } from "lucide-react";
import { getTranslations } from "next-intl/server";
import { LoginForm } from "@/features/auth/LoginForm";
import { AuthShowcasePanel } from "@/components/auth/AuthShowcasePanel";
import { MobileBrandLockup } from "@/components/auth/MobileBrandLockup";
import { LanguageSwitcher } from "@/components/layout/LanguageSwitcher";
import type { AuthLifecycleStop } from "@/components/auth/AuthLifecycleGraphic";

export default async function LoginPage() {
  const tCommon = await getTranslations("common");
  const t = await getTranslations("auth");
  const stops: AuthLifecycleStop[] = [
    { label: t("showcase.booked"), caption: t("showcase.bookedCaption"), tone: "neutral" },
    { label: t("showcase.inTransit"), caption: t("showcase.inTransitCaption"), tone: "motion" },
    { label: t("showcase.outForDelivery"), caption: t("showcase.outForDeliveryCaption"), tone: "highlight" },
    { label: t("showcase.delivered"), caption: t("showcase.deliveredCaption"), tone: "success" },
  ];

  return (
    <main className="relative grid min-h-screen lg:grid-cols-2">
      <AuthShowcasePanel
        icon={Package}
        brand={tCommon("appName")}
        headline={t("showcase.headline")}
        description={t("showcase.description")}
        stops={stops}
      />
      <div className="flex flex-col items-center justify-center gap-6 bg-background p-4">
        <div className="absolute right-4 top-4">
          <LanguageSwitcher />
        </div>
        <div className="flex flex-col items-center gap-2 lg:hidden">
          <MobileBrandLockup icon={Package} brand={tCommon("appName")} />
        </div>
        <LoginForm />
        <p className="text-sm text-muted-foreground">
          {t("login.newHere")}{" "}
          <Link href="/register" className="font-medium text-foreground underline-offset-4 hover:underline">
            {t("login.createOrganization")}
          </Link>
        </p>
      </div>
    </main>
  );
}
