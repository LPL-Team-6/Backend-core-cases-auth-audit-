using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CaseAuth.Api.Contracts;
using CaseAuth.Api.Entities;
using CaseAuth.Api.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CaseAuth.Api.Tests;

public class CaseAuthApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Based on JsonSerializerDefaults.Web (camelCase + case-insensitive property matching,
    // which ReadFromJsonAsync/GetFromJsonAsync otherwise apply implicitly but lose once you
    // supply any custom options) plus a string enum converter to match Program.cs's
    // AddJsonOptions. Without PropertyNameCaseInsensitive, every response silently
    // deserializes to all-default values instead of throwing, since these are records.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private HttpClient ClientFor(string username)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", username);
        return client;
    }

    private static async Task<CaseResponse> CreateCaseReadyForDecisionAsync(HttpClient analyst)
    {
        var created = await analyst.PostAsJsonAsync("/api/cases", new CreateCaseRequest("Jane Doe", null, null, null));
        var c = (await created.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;

        await analyst.PostAsync($"/api/cases/{c.Id}/extract", null);
        await analyst.PostAsync($"/api/cases/{c.Id}/screen", null);
        await analyst.PostAsJsonAsync($"/api/cases/{c.Id}/ai-reviews",
            new CreateAiReviewRequest("demo-classifier", "1.0", AiRecommendation.Approve, "Looks fine."));
        await analyst.PostAsync($"/api/cases/{c.Id}/mark-ai-reviewed", null);
        var afterRequest = await analyst.PostAsync($"/api/cases/{c.Id}/request-decision", null);
        return (await afterRequest.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;
    }

    [Fact]
    public async Task MissingDevUserHeader_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownDevUser_Returns401()
    {
        var response = await ClientFor("nobody").GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CaseFromOneFirm_IsHiddenFromAnotherFirm()
    {
        var analyst1 = ClientFor("analyst1");
        var analyst2 = ClientFor("analyst2");

        var created = await analyst1.PostAsJsonAsync("/api/cases", new CreateCaseRequest("Jane Doe", null, null, null));
        var c = (await created.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;

        var crossFirmGet = await analyst2.GetAsync($"/api/cases/{c.Id}");
        Assert.Equal(HttpStatusCode.NotFound, crossFirmGet.StatusCode);

        var otherFirmList = await analyst2.GetFromJsonAsync<List<CaseResponse>>("/api/cases", JsonOptions);
        Assert.DoesNotContain(otherFirmList!, x => x.Id == c.Id);
    }

    [Fact]
    public async Task MarkAiReviewed_WithoutAnyAiReview_Returns400()
    {
        var analyst = ClientFor("analyst1");
        var created = await analyst.PostAsJsonAsync("/api/cases", new CreateCaseRequest("Jane Doe", null, null, null));
        var c = (await created.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;
        await analyst.PostAsync($"/api/cases/{c.Id}/extract", null);
        await analyst.PostAsync($"/api/cases/{c.Id}/screen", null);

        var response = await analyst.PostAsync($"/api/cases/{c.Id}/mark-ai-reviewed", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RequestDocuments_SendsCaseBackToUploaded()
    {
        var analyst = ClientFor("analyst1");
        var created = await analyst.PostAsJsonAsync("/api/cases", new CreateCaseRequest("Jane Doe", null, null, null));
        var c = (await created.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;
        await analyst.PostAsync($"/api/cases/{c.Id}/extract", null);

        var response = await analyst.PostAsync($"/api/cases/{c.Id}/request-documents", null);
        var updated = (await response.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;

        Assert.Equal(CaseStatus.Uploaded, updated.Status);
    }

    [Fact]
    public async Task Finding_WithSourceFieldIds_LinksBackToExtractedFields()
    {
        var analyst = ClientFor("analyst1");
        var created = await analyst.PostAsJsonAsync("/api/cases", new CreateCaseRequest("Jane Doe", null, null, null));
        var c = (await created.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;

        var pdfPart = new ByteArrayContent("%PDF-1.4 fixture"u8.ToArray());
        pdfPart.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        using var fileContent = new MultipartFormDataContent
        {
            { new StringContent("GovernmentId"), "documentType" },
            { pdfPart, "file", "id.pdf" },
        };
        var uploadResponse = await analyst.PostAsync($"/api/cases/{c.Id}/documents", fileContent);
        var document = (await uploadResponse.Content.ReadFromJsonAsync<DocumentResponse>(JsonOptions))!;

        var fieldResponse = await analyst.PostAsJsonAsync($"/api/documents/{document.Id}/extracted-fields",
            new CreateExtractedFieldsRequest([new ExtractedFieldInput("Address", "123 Main St", 0.9)]));
        var fields = (await fieldResponse.Content.ReadFromJsonAsync<List<ExtractedFieldResponse>>(JsonOptions))!;

        var findingResponse = await analyst.PostAsJsonAsync($"/api/cases/{c.Id}/findings",
            new CreateFindingRequest(FindingSeverity.Medium, FindingSource.Manual, "ADDRESS_MISMATCH",
                "Address differs from the application.", 0.31, [fields[0].Id]));
        var finding = (await findingResponse.Content.ReadFromJsonAsync<FindingResponse>(JsonOptions))!;

        Assert.Equal(HttpStatusCode.Created, findingResponse.StatusCode);
        Assert.Equal([fields[0].Id], finding.SourceFieldIds);
        Assert.Equal(0.31, finding.Score);
    }

    [Fact]
    public async Task DocumentContent_StreamsUploadedFile_OnlyToTheOwningFirm()
    {
        var analyst = ClientFor("analyst1");
        var created = await analyst.PostAsJsonAsync("/api/cases", new CreateCaseRequest("Jane Doe", null, null, null));
        var c = (await created.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;

        var bytes = "%PDF-1.4 fixture content"u8.ToArray();
        var pdfPart = new ByteArrayContent(bytes);
        pdfPart.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        using var fileContent = new MultipartFormDataContent
        {
            { new StringContent("GovernmentId"), "documentType" },
            { pdfPart, "file", "id.pdf" },
        };
        var uploadResponse = await analyst.PostAsync($"/api/cases/{c.Id}/documents", fileContent);
        var document = (await uploadResponse.Content.ReadFromJsonAsync<DocumentResponse>(JsonOptions))!;

        var content = await analyst.GetAsync($"/api/cases/{c.Id}/documents/{document.Id}/content");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("application/pdf", content.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await content.Content.ReadAsByteArrayAsync());

        var otherFirm = await ClientFor("analyst2").GetAsync($"/api/cases/{c.Id}/documents/{document.Id}/content");
        Assert.Equal(HttpStatusCode.NotFound, otherFirm.StatusCode);

        // Only the successful read is audited; the other firm's attempt never reached the file.
        var auditEvents = await analyst.GetFromJsonAsync<List<AuditEventResponse>>($"/api/cases/{c.Id}/audit-events", JsonOptions);
        var viewed = Assert.Single(auditEvents!, e => e.Action == "Document.Viewed");
        Assert.Equal("analyst1", viewed.ActorUsername);
    }

    [Fact]
    public async Task DocumentContent_Returns404_WhenTheStoredFileIsMissing()
    {
        var analyst = ClientFor("analyst1");
        var created = await analyst.PostAsJsonAsync("/api/cases", new CreateCaseRequest("Jane Doe", null, null, null));
        var c = (await created.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;

        var pdfPart = new ByteArrayContent("%PDF-1.4 fixture content"u8.ToArray());
        pdfPart.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        using var fileContent = new MultipartFormDataContent
        {
            { new StringContent("GovernmentId"), "documentType" },
            { pdfPart, "file", "id.pdf" },
        };
        var uploadResponse = await analyst.PostAsync($"/api/cases/{c.Id}/documents", fileContent);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var document = (await uploadResponse.Content.ReadFromJsonAsync<DocumentResponse>(JsonOptions))!;

        var root = factory.Services.GetRequiredService<IOptions<StorageOptions>>().Value.LocalDiskRoot;
        Directory.Delete(Path.Combine(root, c.FirmId, c.Id.ToString()), recursive: true);

        var content = await analyst.GetAsync($"/api/cases/{c.Id}/documents/{document.Id}/content");
        Assert.Equal(HttpStatusCode.NotFound, content.StatusCode);

        var auditEvents = await analyst.GetFromJsonAsync<List<AuditEventResponse>>($"/api/cases/{c.Id}/audit-events", JsonOptions);
        Assert.DoesNotContain(auditEvents!, e => e.Action == "Document.Viewed");
    }

    [Fact]
    public async Task AiReviewInput_CombinesExtractedFieldsAndFindingsForTheCase()
    {
        var analyst = ClientFor("analyst1");
        var created = await analyst.PostAsJsonAsync("/api/cases", new CreateCaseRequest("Jane Doe", null, null, null));
        var c = (await created.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;

        var pdfPart = new ByteArrayContent("%PDF-1.4 fixture"u8.ToArray());
        pdfPart.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        using var fileContent = new MultipartFormDataContent
        {
            { new StringContent("GovernmentId"), "documentType" },
            { pdfPart, "file", "id.pdf" },
        };
        var uploadResponse = await analyst.PostAsync($"/api/cases/{c.Id}/documents", fileContent);
        var document = (await uploadResponse.Content.ReadFromJsonAsync<DocumentResponse>(JsonOptions))!;

        var fieldResponse = await analyst.PostAsJsonAsync($"/api/documents/{document.Id}/extracted-fields",
            new CreateExtractedFieldsRequest([new ExtractedFieldInput("full_name", "John Smyth", 0.97)]));
        var fields = (await fieldResponse.Content.ReadFromJsonAsync<List<ExtractedFieldResponse>>(JsonOptions))!;

        await analyst.PostAsJsonAsync($"/api/cases/{c.Id}/findings",
            new CreateFindingRequest(FindingSeverity.High, FindingSource.Manual, "NAME_MISMATCH",
                "Name on ID differs from application.", 0.89, [fields[0].Id]));

        var inputResponse = await analyst.GetAsync($"/api/cases/{c.Id}/ai-review-input");
        var input = (await inputResponse.Content.ReadFromJsonAsync<AiReviewInputResponse>(JsonOptions))!;

        Assert.Equal(HttpStatusCode.OK, inputResponse.StatusCode);
        Assert.Single(input.Fields, f => f.Id == fields[0].Id && f.DocumentType == DocumentType.GovernmentId);
        Assert.Single(input.Findings, f => f.Code == "NAME_MISMATCH" && f.Score == 0.89 && f.SourceFieldIds.Contains(fields[0].Id));
    }

    [Fact]
    public async Task AnalystCannotApproveDecision()
    {
        var analyst = ClientFor("analyst1");
        var pending = await CreateCaseReadyForDecisionAsync(analyst);

        var response = await SendDecisionAsync(
            analyst, pending.Id, DecisionOutcome.Approved, pending.RowVersion, "analyst-attempt");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Decision_HappyPath_ApprovesCaseAndWritesOneAuditEvent()
    {
        var analyst = ClientFor("analyst1");
        var supervisor = ClientFor("supervisor");
        var pending = await CreateCaseReadyForDecisionAsync(analyst);

        var response = await SendDecisionAsync(
            supervisor, pending.Id, DecisionOutcome.Approved, pending.RowVersion, "key-1");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var updatedCase = await analyst.GetFromJsonAsync<CaseResponse>($"/api/cases/{pending.Id}", JsonOptions);
        Assert.Equal(CaseStatus.Approved, updatedCase!.Status);

        var auditEvents = await analyst.GetFromJsonAsync<List<AuditEventResponse>>($"/api/cases/{pending.Id}/audit-events", JsonOptions);
        Assert.Single(auditEvents!, e => e.Action == "Decision.Approved");
    }

    [Fact]
    public async Task Decision_DuplicateIdempotencyKey_ReplaysResult_WithoutSecondAuditEvent()
    {
        var analyst = ClientFor("analyst1");
        var supervisor = ClientFor("supervisor");
        var pending = await CreateCaseReadyForDecisionAsync(analyst);

        var first = await SendDecisionAsync(supervisor, pending.Id, DecisionOutcome.Approved, pending.RowVersion, "dup-key");
        var firstDecision = (await first.Content.ReadFromJsonAsync<DecisionResponse>(JsonOptions))!;

        var second = await SendDecisionAsync(supervisor, pending.Id, DecisionOutcome.Approved, pending.RowVersion, "dup-key");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondDecision = (await second.Content.ReadFromJsonAsync<DecisionResponse>(JsonOptions))!;
        Assert.Equal(firstDecision.Id, secondDecision.Id);

        var decisions = await analyst.GetFromJsonAsync<List<DecisionResponse>>($"/api/cases/{pending.Id}/decisions", JsonOptions);
        Assert.Single(decisions!);

        var auditEvents = await analyst.GetFromJsonAsync<List<AuditEventResponse>>($"/api/cases/{pending.Id}/audit-events", JsonOptions);
        Assert.Single(auditEvents!, e => e.Action == "Decision.Approved");
    }

    [Fact]
    public async Task Decision_StaleRowVersion_IsRejectedWithConflict()
    {
        var analyst = ClientFor("analyst1");
        var supervisor = ClientFor("supervisor");
        var pending = await CreateCaseReadyForDecisionAsync(analyst);

        var staleRowVersion = Guid.NewGuid();
        var response = await SendDecisionAsync(
            supervisor, pending.Id, DecisionOutcome.Approved, staleRowVersion, "stale-key");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var decisions = await analyst.GetFromJsonAsync<List<DecisionResponse>>($"/api/cases/{pending.Id}/decisions", JsonOptions);
        Assert.Empty(decisions!);
    }

    private static Task<HttpResponseMessage> SendDecisionAsync(
        HttpClient client, Guid caseId, DecisionOutcome outcome, Guid rowVersion, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/cases/{caseId}/decisions")
        {
            Content = JsonContent.Create(new CreateDecisionRequest(outcome, null)),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        // TryAddWithoutValidation: HttpClient's typed If-Match header expects a quoted ETag;
        // the server just reads the raw header string, so skip that client-side parsing.
        request.Headers.TryAddWithoutValidation("If-Match", rowVersion.ToString());
        return client.SendAsync(request);
    }
}
