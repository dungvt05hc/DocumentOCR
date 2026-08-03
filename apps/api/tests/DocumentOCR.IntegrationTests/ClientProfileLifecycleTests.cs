using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentOCR.Application.DTOs;
using DocumentOCR.Domain.Enums;
using Xunit;

namespace DocumentOCR.IntegrationTests;

/// <summary>
/// Drives client-profile CRUD and document↔client assignment through the real WebApi host —
/// same shape as <see cref="DocumentLifecycleTests"/>, but for the ClientProfile feature.
/// </summary>
public class ClientProfileLifecycleTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public ClientProfileLifecycleTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateGetUpdate_ClientProfile_RoundTripsThroughRealHost()
    {
        var client = _factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/clients", new CreateClientProfileRequest
        {
            Name = "Hộ kinh doanh Nguyễn Văn A",
            TaxCode = "0100109106",
            ClientType = ClientType.HouseholdBusiness,
            Address = "123 Lê Lợi, Q1, TP.HCM"
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ClientProfileDto>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal("0100109106", created!.TaxCode);
        Assert.True(created.IsActive);

        var listResponse = await client.GetFromJsonAsync<List<ClientProfileDto>>("/api/clients", JsonOptions);
        Assert.Contains(listResponse!, c => c.Id == created.Id);

        var updateResponse = await client.PutAsJsonAsync($"/api/clients/{created.Id}", new UpdateClientProfileRequest
        {
            Name = "Hộ kinh doanh Nguyễn Văn A (updated)",
            TaxCode = created.TaxCode,
            ClientType = ClientType.HouseholdBusiness,
            Address = created.Address,
            IsActive = false
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<ClientProfileDto>(JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("Hộ kinh doanh Nguyễn Văn A (updated)", updated!.Name);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task Create_DuplicateTaxCodeInSameOrganization_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var request = new CreateClientProfileRequest
        {
            Name = "Client One",
            TaxCode = "0300123456",
            ClientType = ClientType.Enterprise
        };

        var first = await client.PostAsJsonAsync("/api/clients", request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicateRequest = new CreateClientProfileRequest
        {
            Name = "Client Two",
            TaxCode = "0300123456",
            ClientType = ClientType.Enterprise
        };
        var second = await client.PostAsJsonAsync("/api/clients", duplicateRequest);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task AssignDocumentClient_ThenFilterDocumentsByClient_ReturnsOnlyAssignedDocument()
    {
        var client = _factory.CreateClient();

        var clientResponse = await client.PostAsJsonAsync("/api/clients", new CreateClientProfileRequest
        {
            Name = "Assignment Test Client",
            ClientType = ClientType.Enterprise
        });
        var clientProfile = await clientResponse.Content.ReadFromJsonAsync<ClientProfileDto>(JsonOptions);

        var documentId = await UploadOneDocumentAsync(client);

        var assignResponse = await client.PutAsJsonAsync(
            $"/api/documents/{documentId}/client",
            new AssignDocumentClientRequest { ClientProfileId = clientProfile!.Id });
        Assert.Equal(HttpStatusCode.NoContent, assignResponse.StatusCode);

        var docDetail = await client.GetFromJsonAsync<DocumentDetailDto>($"/api/documents/{documentId}", JsonOptions);
        Assert.Equal(clientProfile.Id, docDetail!.ClientProfileId);

        var filtered = await client.GetFromJsonAsync<List<DocumentDto>>(
            $"/api/documents?clientProfileId={clientProfile.Id}", JsonOptions);
        Assert.Contains(filtered!, d => d.Id == documentId);

        var otherClientId = Guid.NewGuid();
        var filteredByOther = await client.GetFromJsonAsync<List<DocumentDto>>(
            $"/api/documents?clientProfileId={otherClientId}", JsonOptions);
        Assert.DoesNotContain(filteredByOther!, d => d.Id == documentId);

        // Unassign
        var unassignResponse = await client.PutAsJsonAsync(
            $"/api/documents/{documentId}/client",
            new AssignDocumentClientRequest { ClientProfileId = null });
        Assert.Equal(HttpStatusCode.NoContent, unassignResponse.StatusCode);

        var afterUnassign = await client.GetFromJsonAsync<DocumentDetailDto>($"/api/documents/{documentId}", JsonOptions);
        Assert.Null(afterUnassign!.ClientProfileId);
    }

    [Fact]
    public async Task AssignDocumentClient_UnknownClientProfile_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        var documentId = await UploadOneDocumentAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/documents/{documentId}/client",
            new AssignDocumentClientRequest { ClientProfileId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<Guid> UploadOneDocumentAsync(HttpClient client)
    {
        using var uploadContent = new MultipartFormDataContent();
        var fileBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 }; // %PDF-1.4
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        uploadContent.Add(fileContent, "files", "invoice.pdf");

        var uploadResponse = await client.PostAsync("/api/documents/upload", uploadContent);
        Assert.Equal(HttpStatusCode.Accepted, uploadResponse.StatusCode);

        var uploadResults = await uploadResponse.Content.ReadFromJsonAsync<List<UploadFileResult>>(JsonOptions);
        var uploadResult = Assert.Single(uploadResults!);
        Assert.True(uploadResult.Success, uploadResult.Error);
        return uploadResult.Document!.Id;
    }
}
