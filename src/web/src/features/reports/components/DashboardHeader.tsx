"use client";

import { useSession } from "@/features/auth/hooks/useSession";

function greeting(hour: number): string {
  if (hour < 12) return "Good morning";
  if (hour < 17) return "Good afternoon";
  return "Good evening";
}

// The dashboard is the one page whose "Page Title" (DESIGN.md's Typography hierarchy) is the
// organization's own name rather than the page's function — every other staff page titles
// itself by what it does ("Shipments," "Branches"); this is the one screen whose job is partly
// to answer "whose data am I looking at," which a literal "Dashboard" label wouldn't do.
export function DashboardHeader() {
  const { data: session } = useSession();
  const firstName = session?.fullName.split(" ")[0];
  const now = new Date();

  return (
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div>
        <p className="text-sm text-muted-foreground">
          {greeting(now.getHours())}
          {firstName ? `, ${firstName}` : ""}
        </p>
        <h1 className="font-display text-xl font-semibold text-foreground">
          {session?.organizationName ?? "Organization Dashboard"}
        </h1>
      </div>
      <p className="text-xs text-muted-foreground">
        As of {now.toLocaleDateString("en-US", { month: "short", day: "numeric" })},{" "}
        {now.toLocaleTimeString("en-US", { hour: "numeric", minute: "2-digit" })}
      </p>
    </div>
  );
}
