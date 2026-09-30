"use client";

import type { ReactNode } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  Sidebar,
  SidebarContent,
  SidebarGroup,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuBadge,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarMenuSkeleton,
  SidebarRail,
  useSidebar,
} from "@/components/ui/sidebar";
import { Skeleton } from "@/components/ui/skeleton";
import { findActiveItem } from "./navModel";
import type { ConsoleBrand, ConsoleNavGroup } from "./types";

// Rough average of how many items a role sees — shown before the real filtered list is known.
const NAV_SKELETON_ROWS = 7;

export function ConsoleSidebar({
  brand,
  scope,
  nav,
  navLoading = false,
}: {
  brand: ConsoleBrand;
  scope?: ReactNode;
  nav: ConsoleNavGroup[];
  navLoading?: boolean;
}) {
  const pathname = usePathname();
  const { state, isMobile, setOpenMobile } = useSidebar();
  const activeHref = findActiveItem(pathname, nav)?.item.href;
  const onNavigate = isMobile ? () => setOpenMobile(false) : undefined;
  const BrandIcon = brand.icon;
  // On mobile, always expand brand and scope since the sidebar renders as a drawer
  const expanded = isMobile || state === "expanded";

  return (
    <Sidebar collapsible="icon">
      <SidebarHeader className="gap-3 pt-4">
        <SidebarMenu>
          <SidebarMenuItem className="flex items-center gap-2.5 px-1 py-1">
            <span className="flex h-[34px] w-[34px] shrink-0 items-center justify-center rounded-lg bg-sidebar-primary">
              <BrandIcon className="h-4 w-4 text-sidebar-primary-foreground" strokeWidth={2} aria-hidden="true" />
            </span>
            {expanded &&
              (brand.isLoading ? (
                <Skeleton className="h-4 w-28 bg-sidebar-accent" data-testid="sidebar-brand-skeleton" />
              ) : (
                <span className="flex min-w-0 flex-col">
                  <span className="truncate font-display text-[15px] font-semibold tracking-tight text-sidebar-accent-foreground">
                    {brand.name}
                  </span>
                  {brand.subtitle && <span className="truncate text-xs text-sidebar-muted">{brand.subtitle}</span>}
                </span>
              ))}
          </SidebarMenuItem>
        </SidebarMenu>
        {expanded && scope}
      </SidebarHeader>
      <SidebarContent>
        <nav aria-label="Main" className="flex flex-col gap-2">
        {navLoading ? (
          <SidebarGroup>
            <SidebarMenu>
              {Array.from({ length: NAV_SKELETON_ROWS }).map((_, i) => (
                <SidebarMenuItem key={i}>
                  <SidebarMenuSkeleton
                    showIcon
                    className="[&>[data-sidebar^=menu-skeleton]]:bg-sidebar-accent"
                    data-testid="nav-item-skeleton"
                  />
                </SidebarMenuItem>
              ))}
            </SidebarMenu>
          </SidebarGroup>
        ) : (
          nav
            .filter((group) => group.items.length > 0)
            .map((group) => (
              <SidebarGroup key={group.id}>
                <SidebarGroupLabel className="font-mono text-[10.5px] uppercase tracking-[0.12em] text-sidebar-muted">
                  {group.label}
                </SidebarGroupLabel>
                <SidebarMenu>
                  {group.items.map((item) => {
                    const Icon = item.icon;
                    const active = item.href === activeHref;
                    return (
                      <SidebarMenuItem key={item.href}>
                        <SidebarMenuButton asChild isActive={active} tooltip={item.label} className="h-[38px] text-[13.5px] [&>svg]:size-[18px]">
                          <Link href={item.href} onClick={onNavigate} aria-current={active ? "page" : undefined}>
                            <Icon strokeWidth={1.75} aria-hidden="true" />
                            <span>{item.label}</span>
                          </Link>
                        </SidebarMenuButton>
                        {item.badge && <SidebarMenuBadge>{item.badge}</SidebarMenuBadge>}
                      </SidebarMenuItem>
                    );
                  })}
                </SidebarMenu>
              </SidebarGroup>
            ))
        )}
        </nav>
      </SidebarContent>
      <SidebarRail />
    </Sidebar>
  );
}
