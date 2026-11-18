using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class MerchantKycEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public MerchantKycEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> RegisterStaffClientAsync(string label)
    {
        var client = _factory.CreateClient();
        var suffix = $"{label}-{Guid.NewGuid():N}"[..12];
        var request = new RegisterOrganizationRequest(
            $"Merchant Org {suffix}", $"merchant-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    private async Task<(Guid MerchantId, HttpClient MerchantClient)> CreateMerchantAndLoginAsync(HttpClient staffClient, string label)
    {
        var ownerEmail = $"owner-{label}-{Guid.NewGuid():N}@example.com";
        var createRequest = new CreateMerchantRequest(
            "Acme Traders Pvt. Ltd.", "Acme Traders", "01-4444444", "contact@acme.example",
            ownerEmail, "P@ssw0rd123!", "Merchant Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu");
        var createResponse = await staffClient.PostAsJsonAsync("/api/v1/merchants", createRequest);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<MerchantResponse>();

        var merchantClient = _factory.CreateClient();
        var loginResponse = await merchantClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, "P@ssw0rd123!"));
        var auth = await loginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();
        merchantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        return (created!.Id, merchantClient);
    }

    [Fact]
    public async Task UploadThenReview_ApprovesDocument()
    {
        var staffClient = await RegisterStaffClientAsync("a");
        var (merchantId, merchantClient) = await CreateMerchantAndLoginAsync(staffClient, "a");

        var uploadContent = new ByteArrayContent(Encoding.UTF8.GetBytes("fake pan card bytes"));
        uploadContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var uploadResponse = await merchantClient.PostAsync($"/api/v1/merchant/kyc-documents?documentType={KycDocumentType.PanCard}", uploadContent);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<MerchantPortalKycDocumentResponse>();
        Assert.Equal(KycStatus.Pending, uploaded!.Status);

        var listResponse = await merchantClient.GetAsync("/api/v1/merchant/kyc-documents");
        var documents = await listResponse.Content.ReadFromJsonAsync<List<MerchantPortalKycDocumentResponse>>();
        Assert.Contains(documents!, d => d.Id == uploaded.Id);

        var reviewResponse = await staffClient.PostAsJsonAsync(
            $"/api/v1/merchants/{merchantId}/kyc-documents/{uploaded.Id}/review", new ReviewKycDocumentRequest(KycStatus.Approved));
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
        var reviewed = await reviewResponse.Content.ReadFromJsonAsync<MerchantKycDocumentResponse>();
        Assert.Equal(KycStatus.Approved, reviewed!.Status);
        Assert.NotNull(reviewed.ReviewedByUserId);
    }

    [Fact]
    public async Task ListKycDocuments_ReturnsNewFields_AndNoReviewerId()
    {
        var staffClient = await RegisterStaffClientAsync("d");
        var (merchantId, merchantClient) = await CreateMerchantAndLoginAsync(staffClient, "d");
        var uploadContent = new ByteArrayContent(Encoding.UTF8.GetBytes("fake pan card bytes"));
        uploadContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var uploadResponse = await merchantClient.PostAsync($"/api/v1/merchant/kyc-documents?documentType={KycDocumentType.PanCard}", uploadContent);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<MerchantPortalKycDocumentResponse>();

        var reviewResponse = await staffClient.PostAsJsonAsync(
            $"/api/v1/merchants/{merchantId}/kyc-documents/{uploaded!.Id}/review",
            new ReviewKycDocumentRequest(KycStatus.Rejected, "Photo is blurry"));
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);

        var listResponse = await merchantClient.GetAsync("/api/v1/merchant/kyc-documents");
        var items = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var item = items.EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == uploaded.Id);

        Assert.True(item.TryGetProperty("uploadedAt", out var uploadedAt));
        Assert.NotEqual(JsonValueKind.Null, uploadedAt.ValueKind);
        Assert.True(item.TryGetProperty("reviewedAt", out var reviewedAt));
        Assert.NotEqual(JsonValueKind.Null, reviewedAt.ValueKind);
        Assert.Equal("Photo is blurry", item.GetProperty("rejectionReason").GetString());
        Assert.False(item.TryGetProperty("reviewedByUserId", out _));
    }

    [Fact]
    public async Task ReviewKycDocument_RejectWithoutReason_Returns400()
    {
        var staffClient = await RegisterStaffClientAsync("e");
        var (merchantId, merchantClient) = await CreateMerchantAndLoginAsync(staffClient, "e");
        var uploadContent = new ByteArrayContent(Encoding.UTF8.GetBytes("fake bytes"));
        uploadContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var uploadResponse = await merchantClient.PostAsync($"/api/v1/merchant/kyc-documents?documentType={KycDocumentType.Other}", uploadContent);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<MerchantPortalKycDocumentResponse>();

        var reviewResponse = await staffClient.PostAsync(
            $"/api/v1/merchants/{merchantId}/kyc-documents/{uploaded!.Id}/review",
            new StringContent("{ \"status\": \"Rejected\" }", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, reviewResponse.StatusCode);
        var body = await reviewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MERCHANT_VALIDATION_FAILED", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Review_FromDifferentOrganization_Returns404()
    {
        var staffClientA = await RegisterStaffClientAsync("b");
        var (merchantId, merchantClient) = await CreateMerchantAndLoginAsync(staffClientA, "b");
        var uploadContent = new ByteArrayContent(Encoding.UTF8.GetBytes("fake bytes"));
        uploadContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var uploadResponse = await merchantClient.PostAsync($"/api/v1/merchant/kyc-documents?documentType={KycDocumentType.Other}", uploadContent);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<MerchantPortalKycDocumentResponse>();

        var staffClientB = await RegisterStaffClientAsync("c");

        var reviewResponse = await staffClientB.PostAsJsonAsync(
            $"/api/v1/merchants/{merchantId}/kyc-documents/{uploaded!.Id}/review", new ReviewKycDocumentRequest(KycStatus.Approved));

        Assert.Equal(HttpStatusCode.NotFound, reviewResponse.StatusCode);
    }
}
