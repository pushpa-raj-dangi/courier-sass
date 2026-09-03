// Backend IdempotencyMiddleware (src/Api/Middleware/IdempotencyMiddleware.cs) requires an
// Idempotency-Key header on every merchant-JWT/API-key POST to /api/v1/shipments,
// /api/v1/shipments/{id}/cancel, and /api/v1/pickups — staff-JWT callers are explicitly exempt.
// Generate one fresh key per user-initiated submission (once per mutationFn call, not per fetch
// retry) so a genuine resubmission is a new logical operation, while any transport-level retry of
// that same fetch call reuses the same key and hits the store's replay path instead of creating a
// duplicate shipment/pickup/cancellation.
export function generateIdempotencyKey(): string {
  return crypto.randomUUID();
}
