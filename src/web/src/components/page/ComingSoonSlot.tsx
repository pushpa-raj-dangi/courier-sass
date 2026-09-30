"use client";

import type { ComponentType } from "react";
import { Hourglass, type LucideProps } from "lucide-react";
import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils";

// The one sanctioned stand-in for data the API does not serve yet (DESIGN.md No-Fake-Numbers
// Rule). `data-coming-soon` makes every slot grep-able when the backend lands.
export function ComingSoonSlot({
  title,
  description,
  icon: Icon = Hourglass,
  minHeight,
  className,
}: {
  title: string;
  description: string;
  icon?: ComponentType<LucideProps>;
  minHeight?: number;
  className?: string;
}) {
  const t = useTranslations("console");
  return (
    <section
      aria-label={title}
      data-coming-soon=""
      style={minHeight ? { minHeight } : undefined}
      className={cn(
        "flex flex-col items-center justify-center gap-2 rounded-xl border border-dashed border-border bg-card p-6 text-center",
        className
      )}
    >
      <span className="flex h-10 w-10 items-center justify-center rounded-lg bg-muted">
        <Icon className="h-5 w-5 text-muted-foreground" strokeWidth={1.75} aria-hidden="true" />
      </span>
      <p className="font-display text-sm font-semibold text-foreground">{title}</p>
      <p className="max-w-sm text-sm text-muted-foreground">{description}</p>
      <span className="font-mono text-[10.5px] uppercase tracking-[0.12em] text-muted-foreground">
        {t("needsBackend")}
      </span>
    </section>
  );
}
