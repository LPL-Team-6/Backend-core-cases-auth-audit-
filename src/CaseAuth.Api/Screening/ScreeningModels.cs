using CaseAuth.Api.Entities;

namespace CaseAuth.Api.Screening;

public sealed record ScreeningDocument(
    Guid Id,
    DocumentType Type);

public sealed record ScreeningField(
    Guid Id,
    Guid DocumentId,
    string Name,
    string Value,
    double? Confidence);

// A field value (address/phone) shared with a document on an unrelated case at the same firm -
// evidence of potential identity reuse across applications. Not wired yet: RuleEngineScreeningService
// always passes an empty array (see its comment), since detecting this needs a case-relationship
// concept this backend doesn't model today.
public sealed record SharedContact(
    Guid SourceFieldId,
    Guid OtherCaseId,
    Guid OtherFieldId,
    string Kind);

public sealed record ScreeningInput(
    Guid CaseId,
    ApplicantKind ApplicantKind,
    string ApplicantName,
    DateOnly? ApplicantDob,
    DateOnly EvaluationDate,
    ScreeningDocument[] Documents,
    ScreeningField[] Fields,
    SharedContact[] SharedContacts);

public sealed record RuleDefinition(
    FindingSeverity Severity,
    double Weight);

public sealed record RuleDraft(
    string Code,
    FindingSeverity Severity,
    string Message,
    double Risk,
    Guid[] SourceFieldIds,
    string EvidenceJson);

public sealed record ScoreContribution(
    string Code,
    double Weight,
    double Risk,
    double Points);

public sealed record EngineResult(
    bool IsComplete,
    double TotalScore,
    string RiskTier,
    RuleDraft[] Findings,
    ScoreContribution[] Contributions);

public sealed class ScreeningOptions
{
    public const string SectionName = "Screening";

    public string RulesetVersion { get; set; } = "illustrative-1.0";

    public double MinimumExtractionConfidence { get; set; } = 0.80;
    public double NearNameThreshold { get; set; } = 0.85;
    public double SanctionsThreshold { get; set; } = 0.90;

    public int MaximumPlausibleAge { get; set; } = 120;
    public int MaximumSnapshotAgeDays { get; set; } = 30;

    public double MediumThreshold { get; set; } = 20;
    public double HighThreshold { get; set; } = 50;

    // Synthetic demo references. Replace with reviewed, versioned data.
    public string[] HighRiskCountryCodes { get; set; } = ["ZZ"];

    public string[] RegisteredAgentAddresses { get; set; } =
        ["900 DEMO AGENT WAY"];

    public string SanctionsMode { get; set; } = "Fixture";
    public string? OfacXmlPath { get; set; }
    public string? OfacSnapshotDate { get; set; }

    public Dictionary<string, RuleDefinition> Rules { get; set; } = new()
    {
        ["LOW_EXTRACTION_CONFIDENCE"] = new(FindingSeverity.Medium, 10),
        ["AMBIGUOUS_FIELD"] = new(FindingSeverity.Medium, 10),
        ["INVALID_FIELD"] = new(FindingSeverity.Medium, 10),
        ["MISSING_REQUIRED_DOCUMENT"] = new(FindingSeverity.High, 25),
        ["MISSING_REQUIRED_FIELD"] = new(FindingSeverity.Medium, 10),

        ["NAME_MISMATCH"] = new(FindingSeverity.High, 25),
        ["DOB_MISMATCH"] = new(FindingSeverity.High, 25),
        ["ADDRESS_MISMATCH"] = new(FindingSeverity.Medium, 10),
        ["TIN_MISMATCH"] = new(FindingSeverity.High, 25),

        ["DOCUMENT_EXPIRED"] = new(FindingSeverity.High, 25),
        ["IMPLAUSIBLE_AGE"] = new(FindingSeverity.High, 25),
        ["INVALID_DATE_RANGE"] = new(FindingSeverity.Medium, 10),

        ["OFAC_POTENTIAL_MATCH"] = new(FindingSeverity.High, 60),
        ["SANCTIONS_UNAVAILABLE"] = new(FindingSeverity.High, 0),

        ["HIGH_RISK_JURISDICTION"] = new(FindingSeverity.Medium, 10),
        ["PO_BOX_ADDRESS"] = new(FindingSeverity.Medium, 10),
        ["REGISTERED_AGENT_ADDRESS"] = new(FindingSeverity.Medium, 10),
        ["MISSING_BENEFICIAL_OWNER"] = new(FindingSeverity.High, 25),

        ["SHARED_ADDRESS"] = new(FindingSeverity.Medium, 10),
        ["SHARED_PHONE"] = new(FindingSeverity.Medium, 10)
    };

    public void Validate()
    {
        static bool Probability(double value) =>
            double.IsFinite(value) && value is >= 0 and <= 1;

        if (!Probability(MinimumExtractionConfidence)
            || !Probability(NearNameThreshold)
            || !Probability(SanctionsThreshold)
            || MaximumPlausibleAge < 1
            || MaximumSnapshotAgeDays < 0
            || !double.IsFinite(MediumThreshold)
            || !double.IsFinite(HighThreshold)
            || MediumThreshold < 0
            || HighThreshold <= MediumThreshold
            || Rules.Values.Any(rule =>
                !double.IsFinite(rule.Weight) || rule.Weight < 0))
        {
            throw new InvalidOperationException(
                "Invalid screening configuration.");
        }
    }
}
