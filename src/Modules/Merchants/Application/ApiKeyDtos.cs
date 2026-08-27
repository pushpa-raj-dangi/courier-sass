namespace Courier.Merchants.Application;

// Scopes null/empty on create means "default to every permission the creating Owner's own role
// grants" (spec §4.1) — not "no permissions". A caller who genuinely wants a narrower key must
// list scopes explicitly; there is no way to request zero scopes.
public record CreateApiKeyRequest(string[]? Scopes = null);

public record ApiKeyResponse(Guid Id, string Prefix, string[] Scopes, bool IsActive, DateTime CreatedAt, DateTime? RevokedAt, DateTime? LastUsedAt);

// PlaintextKey appears here only — the one-time reveal. Never reconstructable from ApiKeyResponse
// or any later read.
public record ApiKeyCreatedResponse(Guid Id, string PlaintextKey, string Prefix, string[] Scopes, DateTime CreatedAt);
