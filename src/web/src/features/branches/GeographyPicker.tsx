"use client";

import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useProvinces, useDistricts, useLocalLevels } from "@/features/geography/hooks/useGeography";

export interface GeographyValue {
  provinceId: string | null;
  districtId: string | null;
  localLevelId: string | null;
  wardNumber: number | null;
}

const LOCAL_LEVEL_TYPE_LABEL: Record<string, string> = {
  Metropolitan: "Metropolitan",
  SubMetropolitan: "Sub-Metropolitan",
  Municipality: "Municipality",
  RuralMunicipality: "Rural Municipality",
};

export function GeographyPicker({
  value,
  onChange,
  idPrefix = "",
}: {
  value: GeographyValue;
  onChange: (next: GeographyValue) => void;
  /** Distinguishes this instance's element ids when the same form renders GeographyPicker more
   * than once (e.g. Recipient + Sender) — without it, every instance shares the same hardcoded
   * ids and every `<label htmlFor>` past the first only ever associates with the first instance. */
  idPrefix?: string;
}) {
  const { data: provinces } = useProvinces();
  const { data: districts } = useDistricts(value.provinceId);
  const { data: localLevels } = useLocalLevels(value.districtId);

  const provinceId = idPrefix ? `${idPrefix}-province` : "province";
  const districtId = idPrefix ? `${idPrefix}-district` : "district";
  const localLevelId = idPrefix ? `${idPrefix}-local-level` : "local-level";
  const wardNumberId = idPrefix ? `${idPrefix}-ward-number` : "ward-number";

  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
      <div className="space-y-2">
        <Label htmlFor={provinceId}>Province</Label>
        <Select
          value={value.provinceId ?? undefined}
          onValueChange={(provinceId) =>
            onChange({ provinceId, districtId: null, localLevelId: null, wardNumber: value.wardNumber })
          }
        >
          <SelectTrigger id={provinceId} aria-label="Province">
            <SelectValue placeholder="Select province" />
          </SelectTrigger>
          <SelectContent>
            {provinces?.map((p) => (
              <SelectItem key={p.id} value={p.id}>
                {p.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-2">
        <Label htmlFor={districtId}>District</Label>
        <Select
          value={value.districtId ?? undefined}
          onValueChange={(districtId) =>
            onChange({ ...value, districtId, localLevelId: null })
          }
          disabled={value.provinceId === null}
        >
          <SelectTrigger id={districtId} aria-label="District">
            <SelectValue placeholder="Select district" />
          </SelectTrigger>
          <SelectContent>
            {districts?.map((d) => (
              <SelectItem key={d.id} value={d.id}>
                {d.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-2">
        <Label htmlFor={localLevelId}>Local Level</Label>
        <Select
          value={value.localLevelId ?? undefined}
          onValueChange={(localLevelId) => onChange({ ...value, localLevelId })}
          disabled={value.districtId === null}
        >
          <SelectTrigger id={localLevelId} aria-label="Local level">
            <SelectValue placeholder="Select local level" />
          </SelectTrigger>
          <SelectContent>
            {localLevels?.map((l) => (
              <SelectItem key={l.id} value={l.id}>
                {l.name} ({LOCAL_LEVEL_TYPE_LABEL[l.type]})
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-2">
        <Label htmlFor={wardNumberId}>Ward Number</Label>
        <Input
          id={wardNumberId}
          type="number"
          min={1}
          value={value.wardNumber ?? ""}
          onChange={(e) =>
            onChange({ ...value, wardNumber: e.target.value === "" ? null : Number(e.target.value) })
          }
        />
      </div>
    </div>
  );
}
