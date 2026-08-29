import { Search } from "lucide-react";

// A visual placeholder for a real search feature (not built yet — nothing in this app to search
// across today). Deliberately not a <button>/<input> and has no onClick or ⌘K listener, so it
// never pretends to respond to a click or a keypress it can't honor.
export function HeaderSearch() {
  return (
    <div
      className="hidden flex-1 max-w-sm items-center gap-2 rounded-md border border-input bg-background px-3 py-1.5 text-muted-foreground sm:flex"
      aria-hidden="true"
    >
      <Search className="h-4 w-4 shrink-0" strokeWidth={2} />
      <span className="flex-1 truncate text-sm">Search or type a command...</span>
      <kbd className="hidden shrink-0 items-center gap-0.5 rounded border border-border bg-muted px-1.5 py-0.5 text-xs font-medium text-muted-foreground sm:inline-flex">
        <span aria-hidden="true">⌘</span>K
      </kbd>
    </div>
  );
}
