import { Package } from "lucide-react";
import { getTranslations } from "next-intl/server";
import { LoginForm } from "@/features/auth/LoginForm";
import { AuthPageShell } from "@/components/auth/AuthPageShell";
import { LanguageSwitcher } from "@/components/layout/LanguageSwitcher";

export default async function LoginPage() {
  const tCommon = await getTranslations("common");
  const t = await getTranslations("auth");

  return (
    <AuthPageShell
      icon={Package}
      brand={tCommon("appName")}
      headline={t("showcase.headline")}
      description={t("showcase.description")}
      switchPrompt={t("login.newHere")}
      switchLabel={t("login.createOrganization")}
      switchHref="/register"
      topAction={<LanguageSwitcher />}
    >
      <LoginForm />
    </AuthPageShell>
  );
}
