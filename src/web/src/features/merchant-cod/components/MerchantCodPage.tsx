"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { PageHeader } from "@/components/page/PageHeader";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { isSettlementPeriod, type SettlementPeriod } from "@/features/merchant-cod/lib/settlementTotals";
import { AwaitingTable } from "./AwaitingTable";
import { CodMoneyStrip } from "./CodMoneyStrip";
import { SettlementsTable } from "./SettlementsTable";

type CodTab = "settlements" | "awaiting";

const PERIOD_LABELS: Record<SettlementPeriod, string> = { "30d": "Last 30 days", "90d": "Last 90 days", all: "All" };

export function MerchantCodPage() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const tab: CodTab = searchParams.get("tab") === "awaiting" ? "awaiting" : "settlements";
  const periodParam = searchParams.get("period");
  const period: SettlementPeriod = isSettlementPeriod(periodParam) ? periodParam : "90d";
  const page = Math.max(1, Number.parseInt(searchParams.get("page") ?? "1", 10) || 1);

  function navigate(next: { tab?: CodTab; period?: SettlementPeriod; page?: number }) {
    const sp = new URLSearchParams();
    const t = next.tab ?? tab;
    const p = next.period ?? period;
    const pg = next.page ?? 1;
    if (t !== "settlements") sp.set("tab", t);
    if (p !== "90d") sp.set("period", p);
    if (pg > 1) sp.set("page", String(pg));
    const qs = sp.toString();
    router.replace(qs ? `${pathname}?${qs}` : pathname, { scroll: false });
  }

  return (
    <div className="space-y-5">
      <PageHeader title="COD" description="Where your cash-on-delivery money is, and what you have been paid." />
      <CodMoneyStrip />

      <div className="flex flex-wrap items-center justify-between gap-3">
        <Tabs value={tab} onValueChange={(v) => navigate({ tab: v as CodTab })}>
          <TabsList>
            <TabsTrigger value="settlements">Settlements</TabsTrigger>
            <TabsTrigger value="awaiting">Awaiting settlement</TabsTrigger>
          </TabsList>
        </Tabs>
        {tab === "settlements" && (
          <Select value={period} onValueChange={(v) => navigate({ period: v as SettlementPeriod })}>
            <SelectTrigger aria-label="Period" className="h-9 w-44">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {(Object.keys(PERIOD_LABELS) as SettlementPeriod[]).map((key) => (
                <SelectItem key={key} value={key}>
                  {PERIOD_LABELS[key]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        )}
      </div>

      {tab === "settlements" ? <SettlementsTable period={period} /> : <AwaitingTable page={page} onPageChange={(pg) => navigate({ page: pg })} />}
    </div>
  );
}
