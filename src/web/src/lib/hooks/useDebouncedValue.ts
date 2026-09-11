"use client";

import { useEffect, useState } from "react";

// No existing debounce precedent in this codebase — this is a minimal, dependency-free hook
// (no new npm package) for delaying a fast-changing value (e.g. search input text) so callers
// like TanStack Query hooks don't refetch on every keystroke.
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const timeout = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(timeout);
  }, [value, delayMs]);

  return debounced;
}
