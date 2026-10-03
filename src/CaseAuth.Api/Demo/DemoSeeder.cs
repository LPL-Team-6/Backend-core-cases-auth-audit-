using System.Text.Json;
using CaseAuth.Api.Auth;
using CaseAuth.Api.Data;
using CaseAuth.Api.Entities;
using CaseAuth.Api.Pipeline;
using CaseAuth.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CaseAuth.Api.Demo;

public interface IDemoSeeder
{
    // Deletes the firm's cases and loads every persona as a fresh case waiting for a decision.
    // Returns the number of cases created.
    Task<int> ResetAsync(string firmId, CancellationToken ct);
}

// Demo support only - DemoController refuses to run outside Development. Audit events are left
// in place on reset (they have no foreign key to Case), so the audit log stays append-only even
// though the case rows it describes are gone.
public class DemoSeeder(
    CaseAuthDbContext db,
    IFileStorageService storage,
    IScreeningService screening,
    IOptions<DemoOptions> options,
    IWebHostEnvironment environment) : IDemoSeeder
{
    public const string SeedActorId = "system";
    public const string SeedActorUsername = "demo-seed";
    public const string AiModelName = "demo-reviewer (seeded)";

    public async Task<int> ResetAsync(string firmId, CancellationToken ct)
    {
        var dataPath = ResolveDataPath();
        var personas = await PersonaLoader.LoadAsync(Path.Combine(dataPath, "personas.json"), ct);

        // Cascades through documents, fields, findings, AI reviews, decisions and pipeline jobs.
        await db.Cases.Where(c => c.FirmId == firmId).ExecuteDeleteAsync(ct);
        await db.Applicants.Where(a => a.FirmId == firmId).ExecuteDeleteAsync(ct);

        // Seeded cases belong to the firm's first analyst so they show up in that analyst's own
        // queue, the way a case they had opened themselves would.
        var owner = DevUserStore.Users.Values.FirstOrDefault(u => u.FirmId == firmId && u.Role == Roles.Analyst)?.Id
            ?? SeedActorId;
        var correlationId = Guid.NewGuid().ToString();
        // Stagger timestamps so each case's audit trail sorts in the order the steps happened.
        // Start in the past: ~100 events at 10ms each would otherwise run a second ahead of the
        // real clock, and anything a user did straight after a reset would sort above the seed.
        var clock = DateTime.UtcNow.AddSeconds(-10);
        DateTime Tick() => clock = clock.AddMilliseconds(10);

        void Audit(Guid? caseId, string action, int? aiReviewVersion = null, string? metadata = null) => db.AuditEvents.Add(new AuditEvent
        {
            Metadata = metadata,
            CaseId = caseId,
            FirmId = firmId,
            ActorUserId = SeedActorId,
            ActorUsername = SeedActorUsername,
            Action = action,
            Outcome = AuditOutcome.Success,
            CorrelationId = correlationId,
            AiReviewVersion = aiReviewVersion,
            Timestamp = Tick(),
        });

        Audit(null, "Demo.Reset");

        // Pass 1: every persona's case, documents and extracted fields. All of them are saved
        // before any screening, so the shared-contact rule can see the other demo cases.
        var seeded = new List<(Persona Persona, Case Case)>();
        var fieldsById = new Dictionary<Guid, ExtractedField>();
        foreach (var p in personas)
        {
            var applicant = new Applicant
            {
                FirmId = firmId,
                FullName = p.Applicant.FullName,
                DateOfBirth = p.Applicant.DateOfBirth,
                Email = p.Applicant.Email,
                Phone = p.Applicant.Phone,
                Kind = p.Applicant.Kind,
            };
            var c = new Case
            {
                FirmId = firmId,
                Applicant = applicant,
                CreatedByUserId = owner,
                CreatedAt = Tick(),
                Status = CaseStatus.AwaitingDecision,
            };
            db.Cases.Add(c);
            seeded.Add((p, c));
            Audit(c.Id, "Case.Created");

            foreach (var d in p.Documents)
            {
                var filePath = Path.Combine(dataPath, "specimens", d.File);
                await using var file = File.OpenRead(filePath);
                var fileName = $"{d.Title}.png";
                var document = new Document
                {
                    Case = c,
                    FileName = fileName,
                    ContentType = "image/png",
                    SizeBytes = file.Length,
                    DocumentType = d.Type,
                    StorageKey = await storage.SaveAsync(firmId, c.Id, fileName, file, ct),
                    UploadedByUserId = owner,
                    UploadedAt = Tick(),
                };
                db.Documents.Add(document);
                Audit(c.Id, "Document.Uploaded");

                foreach (var f in d.Fields)
                {
                    var field = new ExtractedField
                    {
                        Document = document,
                        FieldName = f.Name,
                        FieldValue = f.Value,
                        Confidence = f.Confidence,
                        ExtractedAt = Tick(),
                    };
                    db.ExtractedFields.Add(field);
                    fieldsById[field.Id] = field;
                }
                Audit(c.Id, "ExtractedField.Recorded");
            }
            Audit(c.Id, "Case.extract");
        }
        await db.SaveChangesAsync(ct);

        // Pass 2: the real rules engine flags each case, exactly as the pipeline's Screen job
        // would, then the persona's canned AI review is recorded - and it may only cite codes the
        // engine actually raised.
        foreach (var (p, c) in seeded)
        {
            var evaluation = await screening.ScreenAsync(c.Id, ct);
            if (!evaluation.IsComplete)
            {
                throw new InvalidOperationException("Demo reset needs a usable sanctions snapshot; screening came back incomplete.");
            }

            foreach (var f in evaluation.Findings)
            {
                db.Findings.Add(new Finding
                {
                    Case = c,
                    Code = f.Code,
                    Severity = f.Severity,
                    Source = FindingSource.Deterministic,
                    Message = f.Message,
                    Score = f.Score,
                    EvidenceJson = f.EvidenceJson,
                    SourceFields = f.SourceFieldIds.Distinct().Select(id => fieldsById[id]).ToList(),
                    CreatedAt = Tick(),
                });
                Audit(c.Id, "Finding.Created");
            }
            Audit(c.Id, "Case.screen", metadata: JsonSerializer.Serialize(new
            {
                evaluation.TotalScore,
                evaluation.RiskTier,
                evaluation.RulesetVersion,
                evaluation.SnapshotSource,
                findingCount = evaluation.Findings.Count,
            }));

            var r = p.AiReview;
            var raised = evaluation.Findings.Select(f => f.Code).ToHashSet();
            var badCitation = r.KeyConcerns.SelectMany(k => k.FindingCodes).FirstOrDefault(code => !raised.Contains(code));
            if (badCitation is not null)
            {
                throw new InvalidOperationException(
                    $"Persona '{p.Key}': AI concern cites {badCitation}, which the rules engine didn't raise " +
                    $"(it raised: {string.Join(", ", raised.Order())}).");
            }

            db.AiReviews.Add(new AiReview
            {
                Case = c,
                Version = 1,
                ModelName = AiModelName,
                ModelVersion = "personas-2",
                Recommendation = r.Recommendation,
                Rationale = r.Summary,
                Summary = r.Summary,
                KeyConcerns = r.KeyConcerns.Select(k => new AiConcern(k.Text, k.FindingCodes)).ToList(),
                NextSteps = r.NextSteps,
                DraftCaseNote = r.DraftCaseNote,
                CreatedAt = Tick(),
            });
            Audit(c.Id, "AiReview.Recorded", aiReviewVersion: 1);
            Audit(c.Id, "Case.mark-ai-reviewed");
            Audit(c.Id, "Case.request-decision");
        }

        await db.SaveChangesAsync(ct);
        return personas.Count;
    }

    private string ResolveDataPath()
    {
        if (!string.IsNullOrWhiteSpace(options.Value.DataPath))
        {
            return Path.GetFullPath(options.Value.DataPath, environment.ContentRootPath);
        }

        for (var dir = new DirectoryInfo(environment.ContentRootPath); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "demo");
            if (File.Exists(Path.Combine(candidate, "personas.json")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "Couldn't find demo/personas.json above the content root. Set Demo:DataPath to the demo folder.");
    }
}
