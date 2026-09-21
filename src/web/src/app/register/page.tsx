import { Package } from "lucide-react";
import { getTranslations } from "next-intl/server";
import { RegisterForm } from "@/features/auth/RegisterForm";
import { AuthPageShell } from "@/components/auth/AuthPageShell";
import { LanguageSwitcher } from "@/components/layout/LanguageSwitcher";

export default async function RegisterPage() {
  const tCommon = await getTranslations("common");
  const t = await getTranslations("auth.register");

  return (
    <AuthPageShell
      icon={Package}
      brand={tCommon("appName")}
      headline={t("headline")}
      description={t("description")}
      switchPrompt={t("switchPrompt")}
      switchLabel={t("switchLabel")}
      switchHref="/login"
      topAction={<LanguageSwitcher />}
    >
      <RegisterForm />
    </AuthPageShell>
  );
}
