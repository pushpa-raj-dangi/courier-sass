export interface ShipmentSearchResult {
  awb: string;
  recipientName: string;
  currentStatus: string;
}

export interface BranchSearchResult {
  id: string;
  code: string;
  name: string;
}

export interface ManifestSearchResult {
  id: string;
  code: string;
  status: string;
}

export interface NdrSearchResult {
  id: string;
  awb: string;
  status: string;
}

export interface ReturnShipmentSearchResult {
  id: string;
  returnAwb: string;
  status: string;
}

export interface GlobalSearchResults {
  shipments: ShipmentSearchResult[];
  branches: BranchSearchResult[];
  manifests: ManifestSearchResult[];
  ndrs: NdrSearchResult[];
  returnShipments: ReturnShipmentSearchResult[];
}

export const EMPTY_SEARCH_RESULTS: GlobalSearchResults = {
  shipments: [],
  branches: [],
  manifests: [],
  ndrs: [],
  returnShipments: [],
};
