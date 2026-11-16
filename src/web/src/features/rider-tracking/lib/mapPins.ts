import type { ReactNode } from "react";

export interface MapRiderPin {
  riderId: string;
  latitude: number;
  longitude: number;
  initials: string;
  /** Full name: the marker's hover title. */
  label: string;
  /** Older than STALE_LOCATION_MINUTES: dashed outline plus an "N min" label. */
  stale: boolean;
  minutesAgo: number;
  popup: ReactNode;
}

export interface MapBranchPin {
  latitude: number;
  longitude: number;
  label: string;
}

const ENTITIES: Record<string, string> = { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" };

export function escapeHtml(text: string): string {
  return text.replace(/[&<>"']/g, (c) => ENTITIES[c]);
}

/** Leaflet divIcon HTML (Leaflet renders this string, not React). Tailwind sees these classes here. */
export function riderPinHtml(pin: Pick<MapRiderPin, "initials" | "stale" | "minutesAgo">): string {
  const disc = pin.stale
    ? "border-2 border-dashed border-muted-foreground bg-background text-foreground"
    : "bg-primary text-primary-foreground";
  const age = pin.stale
    ? `<span class="mt-0.5 rounded bg-background px-1 text-2xs text-muted-foreground shadow-sm">${pin.minutesAgo} min</span>`
    : "";
  return `<span class="flex flex-col items-center"><span class="flex h-8 w-8 items-center justify-center rounded-full text-2xs font-semibold ring-2 ring-background ${disc}">${escapeHtml(pin.initials)}</span>${age}</span>`;
}

export function branchPinHtml(label: string): string {
  return `<span class="flex h-6 items-center justify-center rounded-md border border-border bg-card px-1.5 font-mono text-2xs font-semibold text-foreground shadow-sm">${escapeHtml(label)}</span>`;
}
