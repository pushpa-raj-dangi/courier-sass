"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";

/** Page filters live in the URL: read with `params.get`, write a merged patch (undefined deletes). */
export function useSearchParamState() {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();

  function update(patch: Record<string, string | undefined>) {
    const next = new URLSearchParams(params.toString());
    for (const [key, value] of Object.entries(patch)) {
      if (value === undefined) next.delete(key);
      else next.set(key, value);
    }
    const qs = next.toString();
    router.replace(qs ? `${pathname}?${qs}` : pathname, { scroll: false });
  }

  return { params, update };
}
