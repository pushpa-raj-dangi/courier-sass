export interface Province {
  id: string;
  name: string;
}

export interface District {
  id: string;
  provinceId: string;
  name: string;
}

export type LocalLevelType = "Metropolitan" | "SubMetropolitan" | "Municipality" | "RuralMunicipality";

export interface LocalLevel {
  id: string;
  districtId: string;
  name: string;
  type: LocalLevelType;
}

export interface Ward {
  id: string;
  localLevelId: string;
  number: number;
}
