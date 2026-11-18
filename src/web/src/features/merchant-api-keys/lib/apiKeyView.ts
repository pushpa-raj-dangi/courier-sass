import { formatDate } from "@/features/cod/lib/codFormat";
import type { ApiKey } from "../types";

const MIN = 60_000;
const HOUR = 60 * MIN;
const DAY = 24 * HOUR;

function newestFirst(a: ApiKey, b: ApiKey): number {
  return new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime();
}

export function splitKeys(keys: ApiKey[]): { active: ApiKey[]; revoked: ApiKey[] } {
  return {
    active: keys.filter((k) => k.isActive).sort(newestFirst),
    revoked: keys.filter((k) => !k.isActive).sort(newestFirst),
  };
}

export function isRecentlyUsed(lastUsedAt: string | null, now: Date = new Date()): boolean {
  if (!lastUsedAt) return false;
  return now.getTime() - new Date(lastUsedAt).getTime() < DAY;
}

export function lastUsedLabel(lastUsedAt: string | null, now: Date = new Date()): string {
  if (!lastUsedAt) return "Never";
  const elapsed = Math.max(0, now.getTime() - new Date(lastUsedAt).getTime());
  if (elapsed < HOUR) return `${Math.floor(elapsed / MIN)} min ago`;
  if (elapsed < DAY) return `${Math.floor(elapsed / HOUR)} h ago`;
  return formatDate(lastUsedAt);
}
