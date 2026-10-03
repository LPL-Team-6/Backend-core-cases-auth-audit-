using CaseAuth.Api.Auth;
using CaseAuth.Api.Data;
using CaseAuth.Api.Entities;
using CaseAuth.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CaseAuth.Api.Pipeline;

// Does the actual work for one ProcessingJob row: calls the relevant pluggable step interface,
// persists its result, and drives the Case state transition - all through one SaveChangesAsync,
// so the pipeline-step data, the status change, and its audit event commit atomically, exactly
// like the manual HTTP-triggered transitions in CasesController do.
//
// There is no authenticated caller here (this runs on a background thread, not inside a
// request), so audit events are attributed to a fixed "system" actor rather than ICurrentUser,
// and CaseStateMachine is called with a fixed Analyst role rather than a real one.
public class PipelineJobProcessor(
    CaseAuthDbContext db,
    IDocumentExtractor extractor,
    IScreeningService screeningService,
    IAiReviewer aiReviewer,
    ILogger<PipelineJobProcessor> logger) : IPipelineJobProcessor
{
    public const string SystemActorId = "system";
    public const string SystemActorUsername = "pipeline-worker";

    public async Task ProcessAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.ProcessingJobs.Include(j => j.Case).FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null)
        {
            return;
        }

        job.Status = PipelineJobStatus.Processing;
        job.StartedAt = DateTime.UtcNow;
        job.Attempts++;
        await db.SaveChangesAsync(ct);

        try
        {
            switch (job.JobType)
            {
                case PipelineJobType.Extract:
                    await ProcessExtractAsync(job, ct);
                    break;
                case PipelineJobType.Screen:
                    await ProcessScreenAsync(job, ct);
                    break;
                case PipelineJobType.AiReview:
                    await ProcessAiReviewAsync(job, ct);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown pipeline job type '{job.JobType}'.");
            }

            job.Status = PipelineJobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // A ConflictApiException here (e.g. the case already moved on, or moved backwards
            // via request-documents, since this job was enqueued) is an expected outcome, not a
            // bug - it just means this job is stale. Either way the job's Error column records
            // exactly why, instead of a silent drop or a crashed background thread.
            logger.LogError(ex, "Pipeline job {JobId} ({JobType}) failed for case {CaseId}", job.Id, job.JobType, job.CaseId);
            job.Status = PipelineJobStatus.Failed;
            job.Error = ex.Message;
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task ProcessExtractAsync(ProcessingJob job, CancellationToken ct)
    {
        var documents = await db.Documents.Where(d => d.CaseId == job.CaseId).ToListAsync(ct);
        foreach (var document in documents)
        {
            var results = await extractor.ExtractAsync(document, ct);
            foreach (var result in results)
            {
                db.ExtractedFields.Add(new ExtractedField
                {
                    DocumentId = document.Id,
                    FieldName = result.FieldName,
                    FieldValue = result.FieldValue,
                    Confidence = result.Confidence,
                });
            }
        }

        ApplyTransition(job.Case!, "extract", "Pipeline.Extract.Completed");
        await db.SaveChangesAsync(ct);
    }

    private async Task ProcessScreenAsync(ProcessingJob job, CancellationToken ct)
    {
        var result = await screeningService.ScreenAsync(job.CaseId, ct);

        // Keep the findings endpoint on the latest deterministic evaluation after re-screening.
        var previous = await db.Findings
            .Where(finding => finding.CaseId == job.CaseId
                && finding.Source == FindingSource.Deterministic)
            .ToListAsync(ct);
        db.Findings.RemoveRange(previous);

        foreach (var finding in result.Findings)
        {
            var sourceIds = finding.SourceFieldIds.Distinct().ToArray();
            var sourceFields = sourceIds.Length > 0
                ? await db.ExtractedFields
                    .Where(field => sourceIds.Contains(field.Id)
                        && field.Document!.CaseId == job.CaseId)
                    .ToListAsync(ct)
                : [];

            if (sourceFields.Count != sourceIds.Length)
            {
                throw new InvalidOperationException(
                    "Screening evidence must belong to the screened case.");
            }

            db.Findings.Add(new Finding
            {
                CaseId = job.CaseId,
                Code = finding.Code,
                Severity = finding.Severity,
                Source = FindingSource.Deterministic,
                Message = finding.Message,
                Score = finding.Score,
                EvidenceJson = finding.EvidenceJson,
                SourceFields = sourceFields,
            });
        }

        var metadata = System.Text.Json.JsonSerializer.Serialize(new
        {
            result.TotalScore,
            result.RiskTier,
            result.RulesetVersion,
            result.EvaluationDate,
            result.SnapshotSource,
            result.SnapshotDate,
            result.IsComplete,
            findingCount = result.Findings.Count
        });

        if (!result.IsComplete)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                CaseId = job.CaseId,
                FirmId = job.Case!.FirmId,
                ActorUserId = SystemActorId,
                ActorUsername = SystemActorUsername,
                Action = "Pipeline.Screen.Incomplete",
                Outcome = AuditOutcome.Failure,
                CorrelationId = Guid.NewGuid().ToString(),
                Metadata = metadata
            });
            await db.SaveChangesAsync(ct);
            throw new InvalidOperationException(
                "Screening could not complete because sanctions data is unavailable or stale.");
        }

        ApplyTransition(job.Case!, "screen", "Pipeline.Screen.Completed", metadata);
        await db.SaveChangesAsync(ct);
    }

    private async Task ProcessAiReviewAsync(ProcessingJob job, CancellationToken ct)
    {
        var result = await aiReviewer.ReviewAsync(job.CaseId, ct);

        var nextVersion = 1 + await db.AiReviews
            .Where(r => r.CaseId == job.CaseId)
            .Select(r => (int?)r.Version)
            .MaxAsync(ct) ?? 1;

        var keyConcerns = result.KeyConcerns ?? [];
        await AiReviewCitations.EnsureValidAsync(db, job.CaseId, keyConcerns, ct);

        db.AiReviews.Add(new AiReview
        {
            CaseId = job.CaseId,
            Version = nextVersion,
            ModelName = result.ModelName,
            ModelVersion = result.ModelVersion,
            Recommendation = result.Recommendation,
            Rationale = result.Rationale,
            Summary = result.Summary,
            KeyConcerns = keyConcerns,
            NextSteps = result.NextSteps ?? [],
            DraftCaseNote = result.DraftCaseNote,
        });

        // Satisfies the same "at least one AI review must exist" precondition CasesController
        // enforces for the manual /mark-ai-reviewed call - here it's true by construction, since
        // the review above is added before the transition.
        ApplyTransition(job.Case!, "mark-ai-reviewed", "Pipeline.AiReview.Completed");
        await db.SaveChangesAsync(ct);
    }

    private void ApplyTransition(Case c, string action, string auditAction, string? metadata = null)
    {
        var transition = CaseStateMachine.Resolve(action, c.Status, Roles.Analyst);
        c.Status = transition.To;

        db.AuditEvents.Add(new AuditEvent
        {
            CaseId = c.Id,
            FirmId = c.FirmId,
            ActorUserId = SystemActorId,
            ActorUsername = SystemActorUsername,
            Action = auditAction,
            Outcome = AuditOutcome.Success,
            CorrelationId = Guid.NewGuid().ToString(),
            Metadata = metadata,
        });
    }
}
