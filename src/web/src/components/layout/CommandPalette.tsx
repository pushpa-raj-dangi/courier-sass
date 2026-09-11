"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { Plus, Package, Building2, Truck, AlertTriangle, Undo2 } from "lucide-react";
import {
  CommandDialog,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
  CommandSeparator,
} from "@/components/ui/command";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useCommandPaletteStore } from "@/lib/store/commandPaletteStore";
import { NAV_ITEMS, type NavItem } from "@/lib/navigation/navItems";
import { useGlobalSearch } from "@/features/search/hooks/useGlobalSearch";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";
import { useBranchPanelStore } from "@/features/branches/store/branchPanelStore";
import { useManifestPanelStore } from "@/features/manifests/store/manifestPanelStore";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";
import { useNdrPanelStore } from "@/features/ndr/store/ndrPanelStore";
import { useReturnShipmentPanelStore } from "@/features/ndr/store/returnShipmentPanelStore";

function matches(query: string, ...haystack: string[]): boolean {
  if (!query) return true;
  const needle = query.trim().toLowerCase();
  return haystack.some((h) => h.toLowerCase().includes(needle));
}

// The full-app command palette (Ctrl+K / ⌘K), replacing the inert HeaderSearch placeholder.
// shouldFilter is off on the underlying Command — we filter Pages/Quick actions ourselves by
// simple substring match, and entity results already come pre-filtered from useGlobalSearch, so
// cmdk's own fuzzy scoring would only fight both.
export function CommandPalette() {
  const open = useCommandPaletteStore((s) => s.open);
  const setOpen = useCommandPaletteStore((s) => s.setOpen);
  const [query, setQuery] = useState("");
  const router = useRouter();

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        useCommandPaletteStore.getState().toggle();
      }
    }
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, []);

  useEffect(() => {
    if (!open) {
      setQuery("");
    }
  }, [open]);

  function runAndClose(action: () => void) {
    action();
    setOpen(false);
  }

  const canSearchEntities = query.trim().length > 1;
  const { data: results, isFetching } = useGlobalSearch(query);
  const hasAnyEntityResults =
    !!results &&
    (results.shipments.length > 0 ||
      results.branches.length > 0 ||
      results.manifests.length > 0 ||
      results.ndrs.length > 0 ||
      results.returnShipments.length > 0);

  return (
    <CommandDialog open={open} onOpenChange={setOpen} label="Search everything">
      <CommandInput placeholder="Search or type a command..." value={query} onValueChange={setQuery} />
      <CommandList>
        {/* Suppressed once entity search is active: the "isn't available yet" note below already
            explains why the list is empty, so cmdk's generic empty state would just repeat it. */}
        {!canSearchEntities && <CommandEmpty>No results found.</CommandEmpty>}

        <PagesGroup query={query} onNavigate={(href) => runAndClose(() => router.push(href))} />

        {!canSearchEntities && (
          <>
            <CommandSeparator />
            <QuickActionsGroup query={query} onRun={runAndClose} />
          </>
        )}

        {canSearchEntities && (
          <>
            <CommandSeparator />
            <ShipmentResultsGroup results={results?.shipments} onRun={runAndClose} />
            <BranchResultsGroup results={results?.branches} onRun={runAndClose} />
            <ManifestResultsGroup results={results?.manifests} onRun={runAndClose} />
            <NdrResultsGroup results={results?.ndrs} onRun={runAndClose} />
            <ReturnShipmentResultsGroup results={results?.returnShipments} onRun={runAndClose} />
            {!isFetching && !hasAnyEntityResults && (
              <div className="px-4 py-3 text-sm text-muted-foreground">
                Entity search isn&apos;t available yet — try a page name above.
              </div>
            )}
          </>
        )}
      </CommandList>
    </CommandDialog>
  );
}

function PagesGroup({ query, onNavigate }: { query: string; onNavigate: (href: string) => void }) {
  return (
    <CommandGroup heading="Pages">
      {NAV_ITEMS.map((item) => (
        <PageItem key={item.href} item={item} query={query} onNavigate={onNavigate} />
      ))}
    </CommandGroup>
  );
}

function PageItem({
  item,
  query,
  onNavigate,
}: {
  item: NavItem;
  query: string;
  onNavigate: (href: string) => void;
}) {
  const permissions = Array.isArray(item.permission) ? item.permission : [item.permission];
  const hasFirst = usePermission(permissions[0]);
  const hasSecond = usePermission(permissions[1] ?? permissions[0]);
  if (!(hasFirst || hasSecond) || !matches(query, item.label)) {
    return null;
  }
  const Icon = item.icon;
  return (
    <CommandItem value={`page-${item.href}`} onSelect={() => onNavigate(item.href)}>
      <Icon strokeWidth={2} />
      <span>{item.label}</span>
    </CommandItem>
  );
}

interface QuickAction {
  key: string;
  label: string;
  permission: string;
}

function QuickActionsGroup({ query, onRun }: { query: string; onRun: (action: () => void) => void }) {
  const openShipmentCreate = useShipmentPanelStore((s) => s.openCreate);
  const openBranchCreate = useBranchPanelStore((s) => s.openCreate);
  const openManifestCreate = useManifestPanelStore((s) => s.openCreate);
  const openCreateRider = useRiderPanelStore((s) => s.openCreateRider);
  const openCreateVehicle = useRiderPanelStore((s) => s.openCreateVehicle);

  const actions: (QuickAction & { run: () => void })[] = [
    { key: "new-shipment", label: "New Shipment", permission: "shipment.create", run: openShipmentCreate },
    { key: "new-branch", label: "New Branch", permission: "branch.create", run: openBranchCreate },
    { key: "new-manifest", label: "New Manifest", permission: "manifest.create", run: openManifestCreate },
    { key: "new-rider", label: "New Rider", permission: "rider.create", run: openCreateRider },
    { key: "new-vehicle", label: "New Vehicle", permission: "rider.create", run: openCreateVehicle },
  ];

  return (
    <CommandGroup heading="Quick actions">
      {actions.map((action) => (
        <QuickActionItem key={action.key} action={action} query={query} onRun={onRun} />
      ))}
    </CommandGroup>
  );
}

function QuickActionItem({
  action,
  query,
  onRun,
}: {
  action: QuickAction & { run: () => void };
  query: string;
  onRun: (action: () => void) => void;
}) {
  const allowed = usePermission(action.permission);
  if (!allowed || !matches(query, action.label)) {
    return null;
  }
  return (
    <CommandItem value={action.key} onSelect={() => onRun(action.run)}>
      <Plus strokeWidth={2} />
      <span>{action.label}</span>
    </CommandItem>
  );
}

function ShipmentResultsGroup({
  results,
  onRun,
}: {
  results: { awb: string; recipientName: string; currentStatus: string }[] | undefined;
  onRun: (action: () => void) => void;
}) {
  const allowed = usePermission("shipment.read");
  const openDetail = useShipmentPanelStore((s) => s.openDetail);
  if (!allowed || !results || results.length === 0) {
    return null;
  }
  return (
    <CommandGroup heading="Shipments">
      {results.map((r) => (
        <CommandItem key={r.awb} value={`shipment-${r.awb}`} onSelect={() => onRun(() => openDetail(r.awb))}>
          <Package strokeWidth={2} />
          <span>{r.awb}</span>
          <span className="ml-2 truncate text-muted-foreground">{r.recipientName}</span>
        </CommandItem>
      ))}
    </CommandGroup>
  );
}

function BranchResultsGroup({
  results,
  onRun,
}: {
  results: { id: string; code: string; name: string }[] | undefined;
  onRun: (action: () => void) => void;
}) {
  const allowed = usePermission("branch.read");
  const openDetail = useBranchPanelStore((s) => s.openDetail);
  if (!allowed || !results || results.length === 0) {
    return null;
  }
  return (
    <CommandGroup heading="Branches">
      {results.map((r) => (
        <CommandItem key={r.id} value={`branch-${r.id}`} onSelect={() => onRun(() => openDetail(r.id))}>
          <Building2 strokeWidth={2} />
          <span>{r.name}</span>
          <span className="ml-2 truncate text-muted-foreground">{r.code}</span>
        </CommandItem>
      ))}
    </CommandGroup>
  );
}

function ManifestResultsGroup({
  results,
  onRun,
}: {
  results: { id: string; code: string; status: string }[] | undefined;
  onRun: (action: () => void) => void;
}) {
  const allowed = usePermission("manifest.read");
  const openDetail = useManifestPanelStore((s) => s.openDetail);
  if (!allowed || !results || results.length === 0) {
    return null;
  }
  return (
    <CommandGroup heading="Manifests">
      {results.map((r) => (
        <CommandItem key={r.id} value={`manifest-${r.id}`} onSelect={() => onRun(() => openDetail(r.id))}>
          <Truck strokeWidth={2} />
          <span>{r.code}</span>
          <span className="ml-2 truncate text-muted-foreground">{r.status}</span>
        </CommandItem>
      ))}
    </CommandGroup>
  );
}

function NdrResultsGroup({
  results,
  onRun,
}: {
  results: { id: string; awb: string; status: string }[] | undefined;
  onRun: (action: () => void) => void;
}) {
  const allowed = usePermission("ndr.read");
  const openDetail = useNdrPanelStore((s) => s.openDetail);
  if (!allowed || !results || results.length === 0) {
    return null;
  }
  return (
    <CommandGroup heading="NDRs">
      {results.map((r) => (
        <CommandItem key={r.id} value={`ndr-${r.id}`} onSelect={() => onRun(() => openDetail(r.id))}>
          <AlertTriangle strokeWidth={2} />
          <span>{r.awb}</span>
          <span className="ml-2 truncate text-muted-foreground">{r.status}</span>
        </CommandItem>
      ))}
    </CommandGroup>
  );
}

function ReturnShipmentResultsGroup({
  results,
  onRun,
}: {
  results: { id: string; returnAwb: string; status: string }[] | undefined;
  onRun: (action: () => void) => void;
}) {
  const allowed = usePermission("rto.read");
  const openDetail = useReturnShipmentPanelStore((s) => s.openDetail);
  if (!allowed || !results || results.length === 0) {
    return null;
  }
  return (
    <CommandGroup heading="Return Shipments">
      {results.map((r) => (
        <CommandItem key={r.id} value={`return-shipment-${r.id}`} onSelect={() => onRun(() => openDetail(r.id))}>
          <Undo2 strokeWidth={2} />
          <span>{r.returnAwb}</span>
          <span className="ml-2 truncate text-muted-foreground">{r.status}</span>
        </CommandItem>
      ))}
    </CommandGroup>
  );
}
