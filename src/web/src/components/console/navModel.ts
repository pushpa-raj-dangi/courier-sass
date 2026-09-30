import type { ConsoleNavGroup, ConsoleNavGroupDef, ConsoleNavItem } from "./types";

export type GroupableNavItem = ConsoleNavItem & { group: string };

// Buckets already permission-filtered items into their groups, in the groups' declared order.
// A group left with no items is dropped so the rail never shows an orphan heading.
export function groupNavItems(items: GroupableNavItem[], groups: ConsoleNavGroupDef[]): ConsoleNavGroup[] {
  return groups
    .map((group) => ({
      id: group.id,
      label: group.label,
      items: items
        .filter((item) => item.group === group.id)
        .map((item) => ({
          href: item.href,
          label: item.label,
          icon: item.icon,
          badge: item.badge,
          matchPrefix: item.matchPrefix,
        })),
    }))
    .filter((group) => group.items.length > 0);
}

// Longest matching href wins, so /riders/live-map highlights "Live Map" rather than "Riders".
export function findActiveItem(
  pathname: string,
  groups: ConsoleNavGroup[]
): { group: ConsoleNavGroup; item: ConsoleNavItem } | undefined {
  let best: { group: ConsoleNavGroup; item: ConsoleNavItem } | undefined;
  for (const group of groups) {
    for (const item of group.items) {
      const matches = pathname === item.href || (item.matchPrefix === true && pathname.startsWith(`${item.href}/`));
      if (matches && (!best || item.href.length > best.item.href.length)) {
        best = { group, item };
      }
    }
  }
  return best;
}
