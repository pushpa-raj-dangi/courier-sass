"use client";

import { useEffect, useState } from "react";
import { Moon, Sun } from "lucide-react";
import { useTheme } from "next-themes";

// Mirrors ThemeToggle's mount guard: next-themes can't know the persisted theme
// until after hydration, so this stays a plain placeholder icon until mounted to
// avoid a flash of the wrong one. Uses the same shared next-themes state as the
// rest of the app (ThemeToggle/providers.tsx) rather than a marketing-local
// preference, so a visitor's choice here carries through to /login and beyond.
export function MarketingThemeToggle() {
  const { resolvedTheme, setTheme } = useTheme();
  const [mounted, setMounted] = useState(false);

  useEffect(() => {
    setMounted(true);
  }, []);

  return (
    <button
      type="button"
      aria-label={mounted ? (resolvedTheme === "dark" ? "Switch to light mode" : "Switch to dark mode") : "Toggle theme"}
      onClick={() => setTheme(resolvedTheme === "dark" ? "light" : "dark")}
      className="flex h-9 w-9 items-center justify-center rounded-md text-marketing-fg transition-colors hover:bg-marketing-bg-elevated focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg"
    >
      {mounted && resolvedTheme === "dark" ? (
        <Sun className="h-4 w-4" strokeWidth={2} aria-hidden="true" />
      ) : (
        <Moon className="h-4 w-4" strokeWidth={2} aria-hidden="true" />
      )}
    </button>
  );
}
