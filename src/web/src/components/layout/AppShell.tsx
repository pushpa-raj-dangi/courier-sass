"use client";

import type { ReactNode } from "react";
import { Package } from "lucide-react";
import { useTranslations } from "next-intl";
import { ConsoleShell } from "@/components/console/ConsoleShell";
import { BranchSwitcher } from "@/components/layout/BranchSwitcher";
import { CommandPalette } from "@/components/layout/CommandPalette";
import { HeaderSearch } from "@/components/layout/HeaderSearch";
import { ImpersonationBanner } from "@/components/layout/ImpersonationBanner";
import { LanguageSwitcher } from "@/components/layout/LanguageSwitcher";
import { LocaleSync } from "@/components/layout/LocaleSync";
import { ReauthDialog } from "@/components/layout/ReauthDialog";
import { ThemeToggle } from "@/components/layout/ThemeToggle";
import { TrialStatusBanner } from "@/components/layout/TrialStatusBanner";
import { UserMenu } from "@/components/layout/UserMenu";
import { useStaffNavGroups } from "@/components/layout/useStaffNavGroups";
import { Separator } from "@/components/ui/separator";
import { useSession } from "@/features/auth/hooks/useSession";

export function AppShell({ children }: { children: ReactNode }) {
  const tCommon = useTranslations("common");
  const tConsole = useTranslations("console");
  const { data: session, isLoading } = useSession();
  const { groups, isLoading: navLoading } = useStaffNavGroups();

  return (
    <>
      <LocaleSync />
      <ConsoleShell
        brand={{
          icon: Package,
          name: session?.organizationName ?? tCommon("appName"),
          subtitle: tConsole("operationsConsole"),
          isLoading,
        }}
        scope={isLoading ? undefined : <BranchSwitcher />}
        nav={groups}
        navLoading={navLoading}
        headerStart={<HeaderSearch />}
        headerEnd={
          <>
            <LanguageSwitcher />
            <ThemeToggle />
            <Separator orientation="vertical" className="h-6" />
            <UserMenu />
          </>
        }
        banners={
          <>
            <ImpersonationBanner />
            <TrialStatusBanner />
          </>
        }
      >
        {children}
      </ConsoleShell>
      <ReauthDialog />
      <CommandPalette />
    </>
  );
}
