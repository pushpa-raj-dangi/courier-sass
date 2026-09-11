"use client";

import { Search } from "lucide-react";
import { useCommandPaletteStore } from "@/lib/store/commandPaletteStore";

// Opens the global command palette (Ctrl+K / ⌘K) — the actual keyboard listener lives in
// CommandPalette itself so it works from anywhere in the (staff) shell, not just when this
// button has focus.
export function HeaderSearch() {
  const toggle = useCommandPaletteStore((s) => s.toggle);
  return (
    <button
      type="button"
      onClick={toggle}
      className="hidden flex-1 max-w-sm items-center gap-2 rounded-md border border-input bg-background px-3 py-1.5 text-muted-foreground transition-colors hover:bg-accent hover:text-accent-foreground sm:flex"
    >
      <Search className="h-4 w-4 shrink-0" strokeWidth={2} />
      <span className="flex-1 truncate text-left text-sm">Search or type a command...</span>
      <kbd className="hidden shrink-0 items-center gap-0.5 rounded border border-border bg-muted px-1.5 py-0.5 text-xs font-medium text-muted-foreground sm:inline-flex">
        <span aria-hidden="true">⌘</span>K
      </kbd>
    </button>
  );
}
