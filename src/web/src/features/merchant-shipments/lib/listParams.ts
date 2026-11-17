import { toUtcRange } from "@/lib/dates/rangePresets";
import { isMerchantShipmentTab, tabStatuses, type MerchantShipmentTab } from "./statusGroups";

export interface MerchantShipmentListParams {
  tab: MerchantShipmentTab;
  q: string;
  /** "yyyy-MM-dd" local day, or null. */
  from: string | null;
  to: string | null;
  serviceTypeId: string | null;
  page: number;
}

export const EMPTY_MERCHANT_LIST_PARAMS: MerchantShipmentListParams = {
  tab: "all",
  q: "",
  from: null,
  to: null,
  serviceTypeId: null,
  page: 1,
};

export function parseMerchantListParams(sp: URLSearchParams): MerchantShipmentListParams {
  const tab = sp.get("tab");
  const page = Number.parseInt(sp.get("page") ?? "1", 10);
  return {
    tab: isMerchantShipmentTab(tab) ? tab : "all",
    q: sp.get("q") ?? "",
    from: sp.get("from") || null,
    to: sp.get("to") || null,
    serviceTypeId: sp.get("serviceTypeId") || null,
    page: Number.isFinite(page) && page > 0 ? page : 1,
  };
}

export function toMerchantSearchParams(p: MerchantShipmentListParams): URLSearchParams {
  const sp = new URLSearchParams();
  if (p.tab !== "all") sp.set("tab", p.tab);
  if (p.q) sp.set("q", p.q);
  if (p.from) sp.set("from", p.from);
  if (p.to) sp.set("to", p.to);
  if (p.serviceTypeId) sp.set("serviceTypeId", p.serviceTypeId);
  if (p.page > 1) sp.set("page", String(p.page));
  return sp;
}

export function toMerchantApiQuery(p: MerchantShipmentListParams, pageSize: number): URLSearchParams {
  const api = new URLSearchParams();
  api.set("page", String(p.page));
  api.set("pageSize", String(pageSize));
  tabStatuses(p.tab).forEach((s) => api.append("status", s));
  const q = p.q.trim();
  if (q) api.set("q", q);
  if (p.serviceTypeId) api.set("serviceTypeId", p.serviceTypeId);
  const range = toUtcRange(p.from ?? undefined, p.to ?? undefined);
  if (range.from) api.set("createdFrom", range.from);
  if (range.to) api.set("createdTo", range.to);
  return api;
}
