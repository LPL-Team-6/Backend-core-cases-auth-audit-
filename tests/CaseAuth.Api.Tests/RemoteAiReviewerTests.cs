using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CaseAuth.Api.Contracts;
using CaseAuth.Api.Data;
using CaseAuth.Api.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CaseAuth.Api.Tests;

// RemoteAiReviewer against a canned agent response: what it sends, and how the answer maps back.
public class RemoteAiReviewerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed class CannedAgent(Func<string, string> respond) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(LastRequestBody), Encoding.UTF8, "application/json"),
            };
        }
    }

    [Fact]
    public async Task MapsStructuredOutput_WithFindingCodes()
    {
        var supervisor = factory.CreateClient();
        supervisor.DefaultRequestHeaders.Add("X-Dev-User", "supervisor");
        (await supervisor.PostAsync("/api/demo/reset", null)).EnsureSuccessStatusCode();
        var cases = (await supervisor.GetFromJsonAsync<List<CaseResponse>>("/api/cases", JsonOptions))!;
        var smith = cases.Single(c => c.ApplicantFullName == "John Smith");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CaseAuthDbContext>();
        var finding = await db.Findings.FirstAsync(f => f.CaseId == smith.Id);

        var agent = new CannedAgent(body =>
        {
            var caseId = JsonDocument.Parse(body).RootElement.GetProperty("case_id").GetString();
            return JsonSerializer.Serialize(new
            {
                review_id = Guid.NewGuid(), case_id = caseId, version = 1, provider = "bedrock", is_fallback = false,
                fallback_reason = (string?)null, model_id = "test-model", prompt_version = "p1",
                output = new
                {
                    summary = "Name and state differ between documents.",
                    key_concerns = new[] { new { concern = "Surname differs.", cited_finding_ids = new[] { finding.Id.ToString() } } },
                    recommended_next_steps = new[] { "Ask for a second ID." },
                    draft_case_note = "Escalating for a second ID.",
                },
                confidence = new { score = 0.7, band = "medium" },
            });
        });
        var reviewer = new RemoteAiReviewer(
            new HttpClient(agent) { BaseAddress = new Uri("http://agent.test/") }, db, NullLogger<RemoteAiReviewer>.Instance);

        var result = await reviewer.ReviewAsync(smith.Id, CancellationToken.None);

        Assert.Equal("Name and state differ between documents.", result.Summary);
        Assert.Equal([finding.Code], result.KeyConcerns!.Single().FindingCodes);
        Assert.Equal(["Ask for a second ID."], result.NextSteps);
        Assert.Equal("Escalating for a second ID.", result.DraftCaseNote);
    }
}
