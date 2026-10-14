"use client";

import { useState } from "react";

/**
 * Page number that falls back to 1 whenever `filterKey` changes, so narrowing a list from the
 * page header never leaves a tab stranded on a page that no longer exists. Derived during render
 * (no effect, no extra fetch of the stale page).
 */
export function useFilterPage(filterKey: string): [number, (page: number) => void] {
  const [state, setState] = useState({ key: filterKey, page: 1 });
  const page = state.key === filterKey ? state.page : 1;
  return [page, (next: number) => setState({ key: filterKey, page: next })];
}
