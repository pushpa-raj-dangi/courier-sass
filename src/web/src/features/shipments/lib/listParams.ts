import { presetDays, toUtcRange, type RangePreset } from "@/lib/dates/rangePresets";
import { isShipmentTab, tabStatuses, type ShipmentTab } from "./statusGroups";

export type BookedRange = "" | RangePreset | "custom";

export interface ShipmentListParams {
  tab: ShipmentTab;
  /** Explicit statuses (dashboard deep links); win over `tab` when non-empty. */
  statuses: string[];
  q: string;
  branchId: string;
  merchantId: string;
  serviceTypeId: string;
  booked: BookedRange;
  /** YYYY-MM-DD, only used when booked === "custom". */
  from: string;
  to: string;
  page: number;
}

export const EMPTY_LIST_PARAMS: ShipmentListParams = {
  tab: "all",
  statuses: [],
  q: "",
  branchId: "",
  merchantId: "",
  serviceTypeId: "",
  booked: "",
  from: "",
  to: "",
  page: 1,
};

const BOOKED_VALUES: BookedRange[] = ["today", "7d", "30d", "custom"];

export function parseListParams(sp: URLSearchParams): ShipmentListParams {
  const tab = sp.get("tab");
  const booked = sp.get("booked") ?? "";
  const page = Number.parseInt(sp.get("page") ?? "1", 10);
  return {
    tab: isShipmentTab(tab) ? tab : "all",
    statuses: sp.getAll("status"),
    q: sp.get("q") ?? "",
    branchId: sp.get("branchId") ?? "",
    merchantId: sp.get("merchantId") ?? "",
    serviceTypeId: sp.get("serviceTypeId") ?? "",
    booked: (BOOKED_VALUES as string[]).includes(booked) ? (booked as BookedRange) : "",
    from: sp.get("from") ?? "",
    to: sp.get("to") ?? "",
    page: Number.isFinite(page) && page > 0 ? page : 1,
  };
}

export function toSearchParams(p: ShipmentListParams): URLSearchParams {
  const sp = new URLSearchParams();
  if (p.tab !== "all") sp.set("tab", p.tab);
  p.statuses.forEach((s) => sp.append("status", s));
  if (p.q) sp.set("q", p.q);
  if (p.branchId) sp.set("branchId", p.branchId);
  if (p.merchantId) sp.set("merchantId", p.merchantId);
  if (p.serviceTypeId) sp.set("serviceTypeId", p.serviceTypeId);
  if (p.booked) sp.set("booked", p.booked);
  if (p.booked === "custom" && p.from) sp.set("from", p.from);
  if (p.booked === "custom" && p.to) sp.set("to", p.to);
  if (p.page > 1) sp.set("page", String(p.page));
  return sp;
}

export function toApiQuery(p: ShipmentListParams, pageSize: number, now: Date = new Date()): URLSearchParams {
  const api = new URLSearchParams();
  api.set("page", String(p.page));
  api.set("pageSize", String(pageSize));
  const statuses = p.statuses.length > 0 ? p.statuses : tabStatuses(p.tab);
  statuses.forEach((s) => api.append("status", s));
  const q = p.q.trim();
  if (q.length >= 2) api.set("q", q);
  if (p.branchId) api.set("branchId", p.branchId);
  if (p.merchantId) api.set("merchantId", p.merchantId);
  if (p.serviceTypeId) api.set("serviceTypeId", p.serviceTypeId);
  const days = p.booked === "custom" ? { from: p.from, to: p.to } : p.booked ? presetDays(p.booked, now) : { from: "", to: "" };
  const range = toUtcRange(days.from, days.to);
  if (range.from) api.set("createdFrom", range.from);
  if (range.to) api.set("createdTo", range.to);
  return api;
}

export function hasActiveFilters(p: ShipmentListParams): boolean {
  return Boolean(p.q.trim() || p.branchId || p.merchantId || p.serviceTypeId || p.booked || p.statuses.length > 0);
}
