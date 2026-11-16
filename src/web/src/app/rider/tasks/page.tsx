"use client";

import { useState } from "react";
import Link from "next/link";
import { AlertTriangle, ScanLine } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { EmptyState } from "@/components/states/EmptyState";
import { ErrorState } from "@/components/states/ErrorState";
import { useSession } from "@/features/auth/hooks/useSession";
import { NextStopCard } from "@/features/rider-app/components/NextStopCard";
import { RunHeader } from "@/features/rider-app/components/RunHeader";
import { RunTaskRow } from "@/features/rider-app/components/RunTaskRow";
import { useMyToday } from "@/features/rider-app/hooks/useMyToday";
import { groupRunTasks, taskSyncState } from "@/features/rider-app/lib/runModel";
import { useRiderAppPanelStore } from "@/features/rider-app/store/riderAppPanelStore";
import { useOutboxStatus } from "@/features/rider-app/sync/useOutboxStatus";
import { useMyRider } from "@/features/riders/hooks/useMyRider";
import type { MyTodayTask } from "@/features/riders/types";

type RunTab = "deliver" | "pickups" | "done";

const EMPTY: Record<RunTab, string> = {
  deliver: "Nothing left to deliver",
  pickups: "No pickups left",
  done: "Nothing done yet today",
};

export default function RiderRunPage() {
  const { data: rider, isLoading: riderLoading, isError: riderError } = useMyRider();
  const today = useMyToday(!!rider);
  const { data: session } = useSession();
  const { pendingItems, stuckItems } = useOutboxStatus();
  const openRecordAttempt = useRiderAppPanelStore((s) => s.openRecordAttempt);
  const [tab, setTab] = useState<RunTab>("deliver");

  if (riderLoading) {
    return (
      <div className="space-y-3 p-4" role="status" aria-live="polite">
        <span className="sr-only">Loading</span>
        <Skeleton className="h-32 w-full rounded-xl" />
        <Skeleton className="h-24 w-full rounded-xl" />
      </div>
    );
  }
  if (riderError || !rider) {
    return <EmptyState title="No rider profile" description="This account isn't linked to a rider record." />;
  }
  if (today.isError) {
    return <ErrorState title="Couldn't load today's run" description="Check your connection and try again." onRetry={() => today.refetch()} />;
  }

  const data = today.data;
  const groups = data ? groupRunTasks(data.tasks) : undefined;
  const sync = (t: MyTodayTask) => taskSyncState(t.shipmentId, pendingItems, stuckItems);
  const nextDelivery = groups?.toDeliver[0];

  function stops(list: MyTodayTask[], key: RunTab) {
    if (list.length === 0) return <EmptyState title={EMPTY[key]} />;
    if (key === "done") return list.map((t) => <RunTaskRow key={t.taskId} task={t} syncState={sync(t)} />);
    const [first, ...rest] = list;
    return (
      <>
        <NextStopCard task={first} riderId={rider!.id} syncState={sync(first)} />
        {rest.map((t) => (
          <RunTaskRow
            key={t.taskId}
            task={t}
            syncState={sync(t)}
            onOpen={t.type === "Delivery" ? () => openRecordAttempt(t) : undefined}
          />
        ))}
      </>
    );
  }

  return (
    <div className="flex min-h-full flex-col">
      <RunHeader name={session?.fullName} today={data} isLoading={today.isLoading} />
      <div className="flex-1 space-y-3 px-4 pb-32 pt-3">
        {today.isLoading || !data || !groups ? (
          <div className="space-y-2.5" role="status" aria-label="Loading today's stops">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-20 w-full rounded-xl" />
            ))}
          </div>
        ) : !data.run ? (
          <EmptyState title="No run today" description="Tasks assigned to you today will show up here." />
        ) : (
          <Tabs value={tab} onValueChange={(v) => setTab(v as RunTab)}>
            <TabsList className="grid h-11 w-full grid-cols-3">
              <TabsTrigger value="deliver">{`To deliver · ${groups.toDeliver.length}`}</TabsTrigger>
              <TabsTrigger value="pickups">{`Pickups · ${groups.pickups.length}`}</TabsTrigger>
              <TabsTrigger value="done">{`Done · ${groups.done.length}`}</TabsTrigger>
            </TabsList>
            <TabsContent value="deliver" className="space-y-2.5">{stops(groups.toDeliver, "deliver")}</TabsContent>
            <TabsContent value="pickups" className="space-y-2.5">{stops(groups.pickups, "pickups")}</TabsContent>
            <TabsContent value="done" className="space-y-2.5">{stops(groups.done, "done")}</TabsContent>
          </Tabs>
        )}
      </div>
      <div className="fixed inset-x-0 bottom-16 flex gap-2.5 border-t border-border bg-card px-4 py-3">
        <Button asChild size="lg" className="h-14 flex-1 gap-2 text-base">
          <Link href="/rider/scan">
            <ScanLine className="h-5 w-5" aria-hidden="true" />
            Scan parcel
          </Link>
        </Button>
        <Button
          type="button"
          variant="outline"
          size="icon"
          className="h-14 w-14"
          aria-label="Report issue"
          disabled={!nextDelivery}
          onClick={() => nextDelivery && openRecordAttempt(nextDelivery, "fail")}
        >
          <AlertTriangle className="h-5 w-5 text-destructive" aria-hidden="true" />
        </Button>
      </div>
    </div>
  );
}
