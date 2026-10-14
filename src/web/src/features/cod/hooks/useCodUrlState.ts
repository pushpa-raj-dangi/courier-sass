"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { parseCodTab, parseRangePreset, type CodTab } from "@/features/cod/lib/codTabs";
import type { RangePreset } from "@/lib/dates/rangePresets";

export function useCodUrlState() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  function update(patch: Record<string, string | undefined>) {
    const next = new URLSearchParams(searchParams.toString());
    for (const [key, value] of Object.entries(patch)) {
      if (value === undefined) next.delete(key);
      else next.set(key, value);
    }
    const qs = next.toString();
    router.replace(qs ? `${pathname}?${qs}` : pathname, { scroll: false });
  }

  return {
    tab: parseCodTab(searchParams.get("tab")),
    range: parseRangePreset(searchParams.get("range")),
    branchId: searchParams.get("branch") ?? undefined,
    setTab: (tab: CodTab) => update({ tab }),
    setRange: (range: RangePreset) => update({ range }),
    setBranch: (branchId?: string) => update({ branch: branchId }),
  };
}
