"use client";

import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb";
import { findActiveItem } from "./navModel";
import type { ConsoleNavGroup } from "./types";

export function ConsoleBreadcrumb({ nav }: { nav: ConsoleNavGroup[] }) {
  const pathname = usePathname();
  const t = useTranslations("console");
  const active = findActiveItem(pathname, nav);
  if (!active) {
    return null;
  }
  return (
    <Breadcrumb aria-label={t("breadcrumb")} className="hidden sm:block">
      <BreadcrumbList className="text-[13px]">
        <BreadcrumbItem>{active.group.label}</BreadcrumbItem>
        <BreadcrumbSeparator />
        <BreadcrumbItem>
          <BreadcrumbPage className="font-medium">{active.item.label}</BreadcrumbPage>
        </BreadcrumbItem>
      </BreadcrumbList>
    </Breadcrumb>
  );
}
