import type { LucideIcon } from "lucide-react";
import { AuthLifecycleGraphic, type AuthLifecycleStop } from "@/components/auth/AuthLifecycleGraphic";

interface AuthShowcasePanelProps {
  icon: LucideIcon;
  brand: string;
  headline: string;
  description: string;
  stops: AuthLifecycleStop[];
}

/**
 * Left-hand panel for the two auth surfaces (staff `/login`, `/merchant-login`).
 * Hidden below `lg` — on small screens the form alone carries the task, matching
 * the rest of the system's mobile-first-for-task priority. Uses the fixed
 * `auth-panel` tokens (see globals.css), not `bg-primary`, so it stays the same
 * navy gate surface in both light and dark mode.
 */
export function AuthShowcasePanel({ icon: Icon, brand, headline, description, stops }: AuthShowcasePanelProps) {
  return (
    <div className="relative hidden overflow-hidden bg-auth-panel lg:flex lg:flex-col lg:justify-between lg:p-10 xl:p-14">
      <div className="flex items-center gap-2.5">
        <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-auth-panel-foreground/15">
          <Icon className="h-4 w-4 text-auth-panel-foreground" strokeWidth={2} />
        </span>
        <span className="font-display text-base font-semibold tracking-tight text-auth-panel-foreground">{brand}</span>
      </div>

      <div className="max-w-sm">
        {/* h2, not h1: the form's own CardTitle ("Sign in" etc.) is the page's real
            heading and renders even when this panel is hidden below `lg`, so the
            page keeps exactly one h1 regardless of viewport. */}
        <p className="mb-4 font-mono text-[0.6875rem] uppercase tracking-[0.14em] text-auth-panel-foreground/70">
          AWB NP2026-000000 · Sample
        </p>
        <h2 className="font-display text-3xl font-semibold leading-tight tracking-tight text-auth-panel-foreground xl:text-5xl">
          {headline}
        </h2>
        <p className="mt-4 text-sm leading-relaxed text-auth-panel-foreground/70">{description}</p>
      </div>

      <div className="max-w-xs">
        <AuthLifecycleGraphic stops={stops} />
      </div>
    </div>
  );
}
