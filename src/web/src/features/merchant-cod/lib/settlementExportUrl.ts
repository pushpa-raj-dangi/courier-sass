export function settlementExportUrl(id: string): string {
  return `/api/merchant/cod/settlements/${id}/export`;
}
