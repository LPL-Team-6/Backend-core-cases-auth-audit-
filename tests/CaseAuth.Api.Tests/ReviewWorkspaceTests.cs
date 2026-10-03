using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CaseAuth.Api.Contracts;
using CaseAuth.Api.Controllers;
using CaseAuth.Api.Entities;

namespace CaseAuth.Api.Tests;

// Structured AI review output, the analyst's case note, tax-ID masking and upload limits. Own
// class (and database) because it resets the FIRM-A demo data.
public class ReviewWorkspaceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
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

    private async Task<List<CaseResponse>> ResetAsync()
    {
        var reset = await ClientFor("supervisor").PostAsync("/api/demo/reset", null);
        reset.EnsureSuccessStatusCode();
        return (await ClientFor("analyst1").GetFromJsonAsync<List<CaseResponse>>("/api/cases", JsonOptions))!;
    }

    private Task<List<AuditEventResponse>?> AuditAsync(HttpClient client, Guid caseId) =>
        client.GetFromJsonAsync<List<AuditEventResponse>>($"/api/cases/{caseId}/audit-events", JsonOptions);

    [Fact]
    public async Task AiReview_StructuredOutput_RoundTrips_AndMustCiteRealFindings()
    {
        var cases = await ResetAsync();
        var analyst = ClientFor("analyst1");
        var shell = cases.Single(c => c.ApplicantFullName == "Bluewater Meridian Holdings LLC");

        var seeded = (await analyst.GetFromJsonAsync<List<AiReviewResponse>>($"/api/cases/{shell.Id}/ai-reviews", JsonOptions))!.Single();
        Assert.False(string.IsNullOrWhiteSpace(seeded.Summary));
        Assert.NotEmpty(seeded.KeyConcerns);
        Assert.All(seeded.KeyConcerns, k => Assert.NotEmpty(k.FindingCodes));
        Assert.NotEmpty(seeded.NextSteps);
        Assert.False(string.IsNullOrWhiteSpace(seeded.DraftCaseNote));

        var code = seeded.KeyConcerns[0].FindingCodes[0];
        var ok = await analyst.PostAsJsonAsync($"/api/cases/{shell.Id}/ai-reviews", new CreateAiReviewRequest(
            "test-model", "1", AiRecommendation.Escalate, "Second look.",
            Summary: "Still unclear.", KeyConcerns: [new AiConcern("Owner unknown.", [code])], NextSteps: ["Ask for the owner."],
            DraftCaseNote: "Escalated."), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var v2 = (await ok.Content.ReadFromJsonAsync<AiReviewResponse>(JsonOptions))!;
        Assert.Equal(2, v2.Version);
        Assert.Equal([code], v2.KeyConcerns.Single().FindingCodes);

        var invented = await analyst.PostAsJsonAsync($"/api/cases/{shell.Id}/ai-reviews", new CreateAiReviewRequest(
            "test-model", "1", AiRecommendation.Reject, "Made up.",
            KeyConcerns: [new AiConcern("Looks like fraud.", ["NOT_A_REAL_RULE"])]), JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, invented.StatusCode);

        // A plain rationale-only review, as the original contract had it, still records.
        var plain = await analyst.PostAsJsonAsync($"/api/cases/{shell.Id}/ai-reviews", new CreateAiReviewRequest(
            "test-model", "1", AiRecommendation.Escalate, "Rationale only."), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, plain.StatusCode);
    }

    [Fact]
    public async Task CaseNote_StartsFromAiDraft_SavesWithoutStalingTheCase_AndLocksAfterDecision()
    {
        var cases = await ResetAsync();
        var analyst = ClientFor("analyst1");
        var clean = cases.Single(c => c.ApplicantFullName == "Maria Elena Torres");
        var url = $"/api/cases/{clean.Id}/case-note";

        var draft = (await analyst.GetFromJsonAsync<CaseNoteResponse>(url, JsonOptions))!;
        Assert.Equal("AiDraft", draft.Source);
        Assert.Equal(1, draft.BasedOnAiReviewVersion);
        Assert.False(draft.Locked);

        var save = await analyst.PutAsJsonAsync(url, new SaveCaseNoteRequest(draft.Text + " Checked by analyst.", 1));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var saved = (await analyst.GetFromJsonAsync<CaseNoteResponse>(url, JsonOptions))!;
        Assert.Equal("Saved", saved.Source);
        Assert.EndsWith("Checked by analyst.", saved.Text);
        Assert.Equal("u-analyst1", saved.UpdatedByUserId);

        // Autosaving the same text again is a no-op: one audit event, not two.
        await analyst.PutAsJsonAsync(url, new SaveCaseNoteRequest(saved.Text, 1));
        Assert.Single((await AuditAsync(analyst, clean.Id))!, e => e.Action == "CaseNote.Saved");

        // Saving the note leaves the case's RowVersion alone, so a decision opened earlier still lands.
        var afterSave = (await analyst.GetFromJsonAsync<CaseResponse>($"/api/cases/{clean.Id}", JsonOptions))!;
        Assert.Equal(clean.RowVersion, afterSave.RowVersion);

        var decision = new HttpRequestMessage(HttpMethod.Post, $"/api/cases/{clean.Id}/decisions")
        {
            Content = JsonContent.Create(new CreateDecisionRequest(DecisionOutcome.Approved, null)),
        };
        decision.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        decision.Headers.TryAddWithoutValidation("If-Match", clean.RowVersion.ToString());
        Assert.Equal(HttpStatusCode.Created, (await ClientFor("supervisor").SendAsync(decision)).StatusCode);

        Assert.True((await analyst.GetFromJsonAsync<CaseNoteResponse>(url, JsonOptions))!.Locked);
        var late = await analyst.PutAsJsonAsync(url, new SaveCaseNoteRequest("Rewritten after approval.", null));
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);

        // Another firm can't read it.
        Assert.Equal(HttpStatusCode.NotFound, (await ClientFor("analyst2").GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task FullTaxId_IsMaskedEverywhere_AndRevealIsAudited()
    {
        // The demo personas only carry tin_last4, as the rules engine expects; this covers an
        // extractor that records a full TIN anyway.
        var analyst = ClientFor("analyst1");
        var created = await analyst.PostAsJsonAsync("/api/cases", new CreateCaseRequest("Tax Id Case", null, null, null));
        var c = (await created.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;
        var png = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        png.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        using var upload = new MultipartFormDataContent { { new StringContent("W9"), "documentType" }, { png, "file", "w9.png" } };
        var doc = (await (await analyst.PostAsync($"/api/cases/{c.Id}/documents", upload)).Content.ReadFromJsonAsync<DocumentResponse>(JsonOptions))!;
        var recorded = await analyst.PostAsJsonAsync($"/api/documents/{doc.Id}/extracted-fields", new CreateExtractedFieldsRequest(
            [new ExtractedFieldInput("TIN", "987-65-4320", 0.97), new ExtractedFieldInput("FULL_NAME", "TAX ID CASE", 0.97)]));
        var fields = (await recorded.Content.ReadFromJsonAsync<List<ExtractedFieldResponse>>(JsonOptions))!;
        var tin = fields.Single(f => f.FieldName == "TIN");
        Assert.True(tin.IsMasked);
        Assert.Equal("•••••4320", tin.FieldValue);

        var listed = (await analyst.GetFromJsonAsync<List<ExtractedFieldResponse>>($"/api/documents/{doc.Id}/extracted-fields", JsonOptions))!;
        Assert.DoesNotContain(listed, f => f.FieldValue.Contains("987-65-4320"));
        var input = (await analyst.GetFromJsonAsync<AiReviewInputResponse>($"/api/cases/{c.Id}/ai-review-input", JsonOptions))!;
        Assert.Equal("•••••4320", input.Fields.Single(f => f.FieldName == "TIN").FieldValue);

        var reveal = await analyst.PostAsync($"/api/documents/{doc.Id}/extracted-fields/{tin.Id}/reveal", null);
        Assert.Equal(HttpStatusCode.OK, reveal.StatusCode);
        Assert.Equal("987-65-4320", (await reveal.Content.ReadFromJsonAsync<RevealedFieldResponse>(JsonOptions))!.FieldValue);
        var revealEvent = Assert.Single((await AuditAsync(analyst, c.Id))!, e => e.Action == "ExtractedField.Revealed");
        Assert.Equal("analyst1", revealEvent.ActorUsername);

        // Only masked fields can be revealed, and only within the caller's firm.
        var name = fields.Single(f => !f.IsMasked);
        Assert.Equal(HttpStatusCode.NotFound,
            (await analyst.PostAsync($"/api/documents/{doc.Id}/extracted-fields/{name.Id}/reveal", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await ClientFor("analyst2").PostAsync($"/api/documents/{doc.Id}/extracted-fields/{tin.Id}/reveal", null)).StatusCode);
    }

    [Fact]
    public async Task UploadLimits_MatchStorageOptions()
    {
        var limits = (await ClientFor("analyst1").GetFromJsonAsync<UploadLimitsResponse>("/api/upload-limits", JsonOptions))!;
        Assert.True(limits.MaxUploadBytes > 0);
        Assert.Contains("image/png", limits.AllowedContentTypes);
    }
}
