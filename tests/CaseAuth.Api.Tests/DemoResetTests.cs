using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CaseAuth.Api.Contracts;
using CaseAuth.Api.Controllers;
using CaseAuth.Api.Entities;

namespace CaseAuth.Api.Tests;

// Own class (and so its own ApiFactory/database): a reset deletes every FIRM-A case, which would
// pull the rug out from under tests sharing CaseAuthApiTests' database.
public class DemoResetTests(ApiFactory factory) : IClassFixture<ApiFactory>
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

    [Fact]
    public async Task Reset_AsAnalyst_IsForbidden()
    {
        var response = await ClientFor("analyst1").PostAsync("/api/demo/reset", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reset_ReplacesFirmCasesWithPersonas_ReadyForDecision_AndLeavesOtherFirmsAlone()
    {
        var analyst = ClientFor("analyst1");
        var supervisor = ClientFor("supervisor");
        var stale = await analyst.PostAsJsonAsync("/api/cases", new CreateCaseRequest("Old Case", null, null, null));
        var staleCase = (await stale.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;
        var otherFirm = await ClientFor("analyst2").PostAsJsonAsync("/api/cases", new CreateCaseRequest("Firm B Case", null, null, null));
        Assert.Equal(HttpStatusCode.Created, otherFirm.StatusCode);

        var reset = await supervisor.PostAsync("/api/demo/reset", null);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(5, (await reset.Content.ReadFromJsonAsync<DemoResetResponse>(JsonOptions))!.CasesCreated);

        var cases = (await analyst.GetFromJsonAsync<List<CaseResponse>>("/api/cases", JsonOptions))!;
        Assert.Equal(5, cases.Count);
        Assert.DoesNotContain(cases, c => c.Id == staleCase.Id);
        Assert.All(cases, c => Assert.Equal(CaseStatus.AwaitingDecision, c.Status));

        // The rules engine raised the flags, and the case risk is the engine's weighted score.
        var byName = cases.ToDictionary(c => c.ApplicantFullName);
        Assert.Equal(FindingSeverity.High, byName["Bluewater Meridian Holdings LLC"].RiskTier);
        Assert.Equal(FindingSeverity.High, byName["Ruslan Tarkhovsky"].RiskTier);
        Assert.Equal(FindingSeverity.Medium, byName["John Smith"].RiskTier);
        Assert.Equal(0, byName["Maria Elena Torres"].RiskScore);

        async Task<AiReviewInputResponse> InputFor(string name) =>
            (await analyst.GetFromJsonAsync<AiReviewInputResponse>($"/api/cases/{byName[name].Id}/ai-review-input", JsonOptions))!;

        Assert.Empty((await InputFor("Maria Elena Torres")).Findings);
        var smith = await InputFor("John Smith");
        var nameMismatch = smith.Findings.Single(f => f.Code == "NAME_MISMATCH");
        Assert.Contains(smith.Fields, f => nameMismatch.SourceFieldIds.Contains(f.Id) && f.FieldValue == "JOHN SMYTH");
        Assert.Contains(smith.Findings, f => f.Code == "SHARED_PHONE");
        Assert.Contains((await InputFor("Ruslan Tarkhovsky")).Findings, f => f.Code == "OFAC_POTENTIAL_MATCH");
        Assert.Contains((await InputFor("Aisha Rahman")).Findings, f => f.Code == "DOCUMENT_EXPIRED");
        var shell = await InputFor("Bluewater Meridian Holdings LLC");
        Assert.Contains(shell.Findings, f => f.Code == "MISSING_BENEFICIAL_OWNER");
        Assert.Contains(shell.Findings, f => f.Code == "REGISTERED_AGENT_ADDRESS");

        // Document images are readable.
        var shellCase = byName["Bluewater Meridian Holdings LLC"];
        var docs = (await analyst.GetFromJsonAsync<List<DocumentResponse>>($"/api/cases/{shellCase.Id}/documents", JsonOptions))!;
        Assert.Equal(4, docs.Count);
        var image = await analyst.GetAsync($"/api/cases/{shellCase.Id}/documents/{docs[0].Id}/content");
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);

        // Another firm's cases are untouched.
        var firmBCases = (await ClientFor("analyst2").GetFromJsonAsync<List<CaseResponse>>("/api/cases", JsonOptions))!;
        Assert.Single(firmBCases);
    }

    [Fact]
    public async Task CaseRisk_IsTheRulesEnginesWeightedScore()
    {
        var analyst = ClientFor("analyst1");
        var created = await analyst.PostAsJsonAsync("/api/cases", new CreateCaseRequest("Risk Case", null, null, null));
        var c = (await created.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;
        Assert.Equal(0, c.RiskScore);
        Assert.Equal(FindingSeverity.Low, c.RiskTier);

        // Default weights: NAME_MISMATCH 25, ADDRESS_MISMATCH 10. A code that isn't an engine rule
        // carries no weight.
        foreach (var (code, score) in new[] { ("NAME_MISMATCH", 0.5), ("ADDRESS_MISMATCH", 1.0), ("ANALYST_NOTE", 1.0) })
        {
            await analyst.PostAsJsonAsync($"/api/cases/{c.Id}/findings",
                new CreateFindingRequest(FindingSeverity.Medium, FindingSource.Manual, code, "test", score, null));
        }

        var updated = (await analyst.GetFromJsonAsync<CaseResponse>($"/api/cases/{c.Id}", JsonOptions))!;
        Assert.Equal(22.5, updated.RiskScore, precision: 6);
        Assert.Equal(FindingSeverity.Medium, updated.RiskTier);
    }
}
