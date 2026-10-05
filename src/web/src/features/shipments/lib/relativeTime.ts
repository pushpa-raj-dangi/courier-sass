// Compact "Last scan" text: "just now", "12 min ago", "3 h ago", "Yesterday", else a date.
export function formatRelativeTime(iso: string, now: Date = new Date()): string {
  const then = new Date(iso);
  const diffMin = Math.floor((now.getTime() - then.getTime()) / 60000);
  if (diffMin < 1) return "just now";
  if (diffMin < 60) return `${diffMin} min ago`;
  const diffH = Math.floor(diffMin / 60);
  if (diffH < 24) return `${diffH} h ago`;
  if (diffH < 48) return "Yesterday";
  return then.toLocaleDateString();
}
