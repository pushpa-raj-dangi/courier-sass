export interface Country {
  id: string;
  isoCode: string;
  nameEn: string;
  nameNe: string | null;
  defaultCurrency: string;
  callingCode: string;
  isServiced: boolean;
  countryZoneId: string | null;
  countryZoneName: string | null;
}

export interface CountryZone {
  id: string;
  name: string;
  description: string | null;
}
