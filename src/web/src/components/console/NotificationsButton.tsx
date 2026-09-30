"use client";

import { Bell } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";

// No notification feed exists yet (backend follow-up), so the bell has no unread dot and
// opens a ComingSoonSlot rather than an empty list pretending to be real.
export function NotificationsButton() {
  const t = useTranslations("console");
  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button variant="outline" size="icon" className="h-10 w-10 rounded-full" aria-label={t("notifications")}>
          <Bell className="h-4 w-4" strokeWidth={1.8} aria-hidden="true" />
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-80 p-2">
        <ComingSoonSlot
          title={t("notificationsTitle")}
          description={t("notificationsDescription")}
          icon={Bell}
          className="border-0"
        />
      </PopoverContent>
    </Popover>
  );
}
