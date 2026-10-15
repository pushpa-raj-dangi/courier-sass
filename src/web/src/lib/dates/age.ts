/** Hours after which an open item (rider-held cash, an NDR) counts as overdue. */
export const AGE_WARNING_HOURS = 24;

export function ageHours(sinceIso: string, now: Date = new Date()): number {
  return Math.max(0, (now.getTime() - Date.parse(sinceIso)) / 3_600_000);
}

/** "< 1 h", "7 h", "1 d 20 h", "2 d". */
export function formatAge(sinceIso: string, now: Date = new Date()): string {
  const hours = Math.floor(ageHours(sinceIso, now));
  if (hours < 1) return "< 1 h";
  if (hours < 24) return `${hours} h`;
  const days = Math.floor(hours / 24);
  const rest = hours % 24;
  return rest === 0 ? `${days} d` : `${days} d ${rest} h`;
}
