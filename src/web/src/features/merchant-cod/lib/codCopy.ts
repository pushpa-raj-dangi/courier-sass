export function plural(n: number, singular: string, pluralForm = `${singular}s`): string {
  return `${n} ${n === 1 ? singular : pluralForm}`;
}

/** Caption for the "In settlement" figure — count is the number of Created settlements, not parcels. */
export function settlementsCaption(count: number): string {
  return `${plural(count, "settlement")} awaiting payout`;
}

export function deliveredCodParcelsCaption(count: number): string {
  return plural(count, "delivered COD parcel");
}
