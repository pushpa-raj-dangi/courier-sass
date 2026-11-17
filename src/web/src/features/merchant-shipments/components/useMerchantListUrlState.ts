"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import {
  parseMerchantListParams,
  toMerchantSearchParams,
  type MerchantShipmentListParams,
} from "@/features/merchant-shipments/lib/listParams";

/** URL-backed list params. Any change other than `page` resets to page 1. */
export function useMerchantListUrlState() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const params = parseMerchantListParams(new URLSearchParams(searchParams.toString()));

  function navigate(next: MerchantShipmentListParams) {
    const qs = toMerchantSearchParams(next).toString();
    router.replace(qs ? `${pathname}?${qs}` : pathname, { scroll: false });
  }
  function patch(change: Partial<MerchantShipmentListParams>) {
    navigate({ ...params, ...change, page: 1 });
  }
  function setPage(page: number) {
    navigate({ ...params, page });
  }

  return { params, patch, setPage };
}
