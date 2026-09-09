"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { readImpersonationMeta, type ImpersonationMeta } from "@/lib/impersonation";

function formatRemaining(expiresAt: string): string {
  const remainingMs = new Date(expiresAt).getTime() - Date.now();
  if (remainingMs <= 0) {
    return "0:00";
  }
  const totalSeconds = Math.floor(remainingMs / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${seconds.toString().padStart(2, "0")}`;
}

// Amber/warning-toned inline colors here are deliberate and independent of the app's theme
// tokens — an impersonation banner needs to read as an unmistakable, unambiguous warning in
// both light and dark mode, the same reasoning DESIGN.md already applies to other fixed,
// mode-independent surfaces like the marketing site's dark identity.
export function ImpersonationBanner() {
  const router = useRouter();
  const [meta, setMeta] = useState<ImpersonationMeta | null>(null);
  const [, forceTick] = useState(0);

  useEffect(() => {
    setMeta(readImpersonationMeta());
    const interval = setInterval(() => {
      forceTick((n) => n + 1);
      setMeta(readImpersonationMeta());
    }, 1000);
    return () => clearInterval(interval);
  }, []);

  if (!meta) {
    return null;
  }

  const endSession = async () => {
    await fetch("/api/impersonation/end", { method: "POST" });
    router.push("/platform/organizations");
  };

  return (
    <div className="flex items-center justify-between bg-amber-500 px-4 py-2 text-sm font-medium text-amber-950">
      <span>
        Impersonating {meta.name} at {meta.organizationName} — ends in {formatRemaining(meta.expiresAt)}
      </span>
      <Button size="sm" variant="outline" className="h-7 border-amber-950/30 bg-transparent text-amber-950 hover:bg-amber-950/10" onClick={endSession}>
        End session
      </Button>
    </div>
  );
}
