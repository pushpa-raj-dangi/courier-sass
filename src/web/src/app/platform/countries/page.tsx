"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { usePlatformCountries } from "@/features/platform-countries/hooks/usePlatformCountries";
import { useUpdateCountry } from "@/features/platform-countries/hooks/useUpdateCountry";
import { usePlatformCountryZones } from "@/features/platform-countries/hooks/usePlatformCountryZones";
import { useCountryZonePanelStore } from "@/features/platform-countries/store/countryZonePanelStore";
import { CountryZoneSheet } from "@/features/platform-countries/CountryZoneSheet";
import { useDebouncedValue } from "@/lib/hooks/useDebouncedValue";
import type { Country } from "@/features/platform-countries/types";

const PAGE_SIZE = 25;
const NO_ZONE = "none";

export default function PlatformCountriesPage() {
  const [searchInput, setSearchInput] = useState("");
  const [page, setPage] = useState(1);
  const debouncedSearch = useDebouncedValue(searchInput, 300);
  const { data, isLoading, isError, refetch } = usePlatformCountries(debouncedSearch, page, PAGE_SIZE);
  const { data: zones } = usePlatformCountryZones();
  const updateCountry = useUpdateCountry();
  const openCreateZone = useCountryZonePanelStore((s) => s.openCreate);
  const openEditZone = useCountryZonePanelStore((s) => s.openEdit);

  const columns: ColumnDef<Country>[] = [
    { key: "isoCode", header: "Code", render: (c) => c.isoCode },
    { key: "name", header: "Name", render: (c) => c.nameNe ?? c.nameEn },
    { key: "currency", header: "Currency", render: (c) => c.defaultCurrency },
    {
      key: "zone",
      header: "Zone",
      render: (c) => (
        <Select
          value={c.countryZoneId ?? NO_ZONE}
          onValueChange={(value) => updateCountry.mutate({ id: c.id, isServiced: c.isServiced, countryZoneId: value === NO_ZONE ? null : value })}
        >
          <SelectTrigger className="w-40" aria-label={`Zone for ${c.nameEn}`}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={NO_ZONE}>No zone</SelectItem>
            {zones?.map((z) => (
              <SelectItem key={z.id} value={z.id}>{z.name}</SelectItem>
            ))}
          </SelectContent>
        </Select>
      ),
    },
    {
      key: "serviced",
      header: "Serviced",
      render: (c) => (
        <Switch
          checked={c.isServiced}
          aria-label={`Toggle serviced for ${c.nameEn}`}
          onCheckedChange={(checked) => updateCountry.mutate({ id: c.id, isServiced: checked, countryZoneId: c.countryZoneId })}
        />
      ),
    },
  ];

  if (isError) {
    return (
      <ErrorState
        title="Couldn't load countries"
        description="Something went wrong fetching the country list."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-6 p-6">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Countries</h1>
      </div>
      <Input
        value={searchInput}
        onChange={(e) => {
          setSearchInput(e.target.value);
          setPage(1);
        }}
        placeholder="Search by code or name"
        className="max-w-sm"
        aria-label="Search countries"
      />
      <DataTable
        columns={columns}
        data={data}
        isLoading={isLoading}
        emptyTitle="No countries found"
        emptyDescription="Try a different search term."
        onPageChange={setPage}
        rowKey={(c) => c.id}
      />

      <div className="flex items-center justify-between border-t pt-6">
        <h2 className="font-display text-lg font-semibold">Country Zones</h2>
        <Button size="sm" className="gap-1.5" onClick={() => openCreateZone()}>
          <Plus className="h-4 w-4" strokeWidth={2} />
          New Zone
        </Button>
      </div>
      <ul className="space-y-2">
        {zones?.map((zone) => (
          <li key={zone.id} className="flex items-center justify-between rounded-md border p-3 text-sm">
            <div>
              <div className="font-medium">{zone.name}</div>
              {zone.description && <div className="text-muted-foreground">{zone.description}</div>}
            </div>
            <Button size="sm" variant="ghost" onClick={() => openEditZone(zone.id)}>
              Edit
            </Button>
          </li>
        ))}
        {zones?.length === 0 && <li className="text-sm text-muted-foreground">No zones yet.</li>}
      </ul>

      <CountryZoneSheet />
    </div>
  );
}
