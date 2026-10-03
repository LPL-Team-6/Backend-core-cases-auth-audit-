using CaseAuth.Api.Data;
using CaseAuth.Api.Entities;
using CaseAuth.Api.Screening;
using Microsoft.EntityFrameworkCore;

namespace CaseAuth.Api.Pipeline;

// Wires IScreeningService to Teammate 3's actual rules engine (../Screening/ScreeningEngine.cs,
// ported from the Team3-Screening-and-rules-engine repo's nam-branch). Unlike RemoteAiReviewer,
// this isn't a network seam - the engine is a pure, in-process rule evaluator that Teammate 3
// built directly against this repo's own entities, so it runs in the same process and
// transaction as the rest of the pipeline.
//
// Teammate 3's engine also supports business ("Entity") applicants and cross-case shared-
// contact detection, but this backend only models individual applicants (Applicant has no Kind
// column) and has no case-relationship concept - so ApplicantKind is always Individual and
// SharedContacts is always empty here. Revisit both if those features land.
public class RuleEngineScreeningService(
    CaseAuthDbContext db,
    ScreeningEngine engine,
    ScreeningOptions options,
    TimeProvider clock) : IScreeningService
{
    public async Task<IReadOnlyList<ScreeningFindingResult>> ScreenAsync(Guid caseId, CancellationToken ct)
    {
        var applicant = await db.Cases.AsNoTracking()
            .Where(c => c.Id == caseId)
            .Select(c => c.Applicant)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Case '{caseId}' was not found.");

        var documents = await db.Documents.AsNoTracking()
            .Where(d => d.CaseId == caseId)
            .OrderBy(d => d.Id)
            .Select(d => new ScreeningDocument(d.Id, d.DocumentType))
            .ToArrayAsync(ct);

        var documentIds = documents.Select(d => d.Id).ToArray();

        var fields = await db.ExtractedFields.AsNoTracking()
            .Where(f => documentIds.Contains(f.DocumentId))
            .OrderBy(f => f.DocumentId).ThenBy(f => f.Id)
            .Select(f => new ScreeningField(f.Id, f.DocumentId, Normalization.Key(f.FieldName), f.FieldValue, f.Confidence))
            .ToArrayAsync(ct);

        var evaluationDate = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var input = new ScreeningInput(
            CaseId: caseId,
            ApplicantKind: ApplicantKind.Individual,
            ApplicantName: applicant!.FullName,
            ApplicantDob: applicant.DateOfBirth,
            EvaluationDate: evaluationDate,
            Documents: documents,
            Fields: fields,
            SharedContacts: []);

        var snapshot = SanctionsSnapshots.FromConfiguration(options, clock);

        var result = engine.Evaluate(input, snapshot);

        return result.Findings
            .Select(finding => new ScreeningFindingResult(
                finding.Code,
                finding.Severity,
                finding.Message,
                finding.Risk,
                finding.SourceFieldIds))
            .ToList();
    }
}
