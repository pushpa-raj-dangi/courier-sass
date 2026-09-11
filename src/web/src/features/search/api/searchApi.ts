import { EMPTY_SEARCH_RESULTS, type GlobalSearchResults } from "@/features/search/types";

// TODO(backend): no GET /api/v1/search endpoint exists yet (CLAUDE.md §25/§40 Phase 13). Once one
// does, replace this with `clientFetch<GlobalSearchResults>(`/api/search?q=${encodeURIComponent(query)}`)`
// — useGlobalSearch already wraps this in useQuery, so that's the only line that needs to change.
export async function searchEntities(query: string): Promise<GlobalSearchResults> {
  void query;
  return EMPTY_SEARCH_RESULTS;
}
