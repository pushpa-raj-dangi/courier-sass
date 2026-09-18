using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

// Confirms the fix in Task 7 actually works end-to-end: rotating Jwt.SigningKey through the
// platform settings API must invalidate tokens signed with the OLD key and accept tokens signed
// with the NEW one — same-process, since PlatformSettingsService.UpdateAsync calls
// PlatformSettingsReloader.ReloadAsync() synchronously before returning (no Redis round-trip
// needed for THIS process to observe its own change), so this test is deterministic.
//
// Known gap (deliberately out of scope here): this test performs a REAL rotation of the shared
// Jwt.SigningKey and broadcasts on the real, shared `platform-settings:changed` Redis channel
// (PlatformSettingsChangeListener.ChannelName is a hardcoded const, not configurable). It does
// NOT isolate that broadcast from other concurrently-running WebApplicationFactory hosts in this
// suite, so a concurrent run could see another host's cache reload mid-test. The assertions below
// don't depend on that reload, since this process's own cache update happens synchronously inside
// PlatformSettingsService.UpdateAsync/ResetAsync — but cross-test-host interference remains a
// known, separately-tracked risk, not something fixed by this test.
public class PlatformSettingsJwtRotationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlatformSettingsJwtRotationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<string> LoginAsSuperAdminAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task RotatingSigningKey_OldTokenRejected_NewTokenAccepted()
    {
        var client = _factory.CreateClient();
        var oldToken = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldToken);

        try
        {
            var rotateResponse = await client.PutAsJsonAsync("/api/v1/platform/settings/Jwt.SigningKey",
                new { value = $"rotated-signing-key-{Guid.NewGuid():N}-needs-to-be-long-enough-for-hmacsha256", confirmationPhrase = "REVOKE ALL SESSIONS" });
            Assert.Equal(HttpStatusCode.NoContent, rotateResponse.StatusCode);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldToken);
            var oldTokenResponse = await client.GetAsync("/api/v1/platform/settings");
            Assert.Equal(HttpStatusCode.Unauthorized, oldTokenResponse.StatusCode);

            var newClient = _factory.CreateClient();
            var newToken = await LoginAsSuperAdminAsync(newClient);
            newClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
            var newTokenResponse = await newClient.GetAsync("/api/v1/platform/settings");
            Assert.Equal(HttpStatusCode.OK, newTokenResponse.StatusCode);
        }
        finally
        {
            // This test rotates the REAL Jwt.SigningKey used by every OTHER test in this shared-
            // Postgres suite that logs in via any of the 3 token tracks (staff/platform/merchant).
            // The reset MUST run even if an assertion above throws, or every subsequent test in
            // this (and possibly a concurrent) run would fail to authenticate. Reset via a fresh
            // client logged in with the SuperAdmin's fixed password (unaffected by JWT rotation,
            // since it's not a signed token) so this cleanup doesn't itself depend on either the
            // old or new signing key still being valid.
            var cleanupClient = _factory.CreateClient();
            var cleanupToken = await LoginAsSuperAdminAsync(cleanupClient);
            cleanupClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanupToken);
            await cleanupClient.PostAsync("/api/v1/platform/settings/Jwt.SigningKey/reset", null);
        }
    }
}
