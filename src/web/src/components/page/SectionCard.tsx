import type { ComponentType, ReactNode } from "react";
import type { LucideProps } from "lucide-react";
import { Card, CardContent, CardHeader } from "@/components/ui/card";
import { cn } from "@/lib/utils";

export function SectionCard({
  title,
  icon: Icon,
  description,
  action,
  className,
  children,
}: {
  title: string;
  icon?: ComponentType<LucideProps>;
  description?: ReactNode;
  action?: ReactNode;
  className?: string;
  children: ReactNode;
}) {
  return (
    <Card className={cn("flex flex-col", className)}>
      <CardHeader className="flex flex-row items-start justify-between gap-3 space-y-0 p-5 pb-3">
        <div className="flex min-w-0 items-center gap-3">
          {Icon && (
            <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-muted">
              <Icon className="h-4 w-4 text-muted-foreground" strokeWidth={2} aria-hidden="true" />
            </span>
          )}
          <div className="min-w-0 space-y-1">
            <h2 className="font-display text-base font-semibold leading-none text-foreground">{title}</h2>
            {description && <p className="text-xs text-muted-foreground">{description}</p>}
          </div>
        </div>
        {action && <div className="shrink-0 text-sm">{action}</div>}
      </CardHeader>
      <CardContent className="flex-1 p-5 pt-0">{children}</CardContent>
    </Card>
  );
}
