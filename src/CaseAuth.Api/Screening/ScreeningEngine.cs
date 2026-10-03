using System.Text.Json;
using CaseAuth.Api.Entities;

namespace CaseAuth.Api.Screening;

public sealed class ScreeningEngine(ScreeningOptions options)
{
    private readonly NameMatcher _names = new();

    private sealed class PendingFinding
    {
        public required string Code { get; init; }
        public double Risk { get; set; }

        public HashSet<Guid> Fields { get; } = [];
        public List<string> Messages { get; } = [];
        public List<object> Details { get; } = [];
    }

    public EngineResult Evaluate(
        ScreeningInput input,
        SanctionsSnapshot snapshot)
    {
        var pending = new Dictionary<string, PendingFinding>(
            StringComparer.Ordinal);

        void Add(
            string code,
            string message,
            IEnumerable<Guid>? fieldIds = null,
            double risk = 1,
            object? details = null)
        {
            if (!pending.TryGetValue(code, out var finding))
            {
                finding = new PendingFinding
                {
                    Code = code,
                    Risk = risk
                };

                pending.Add(code, finding);
            }

            finding.Risk = Math.Max(finding.Risk, risk);

            if (!finding.Messages.Contains(message, StringComparer.Ordinal))
            {
                finding.Messages.Add(message);
            }

            foreach (var id in fieldIds ?? [])
            {
                finding.Fields.Add(id);
            }

            if (details is not null)
            {
                finding.Details.Add(details);
            }
        }

        var fields = input.Fields
            .Select(field => field with
            {
                Name = Normalization.Key(field.Name)
            })
            .OrderBy(field => field.DocumentId)
            .ThenBy(field => field.Name, StringComparer.Ordinal)
            .ThenBy(field => field.Id)
            .ToArray();

        var ambiguous = fields
            .GroupBy(field => (field.DocumentId, field.Name))
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .Select(field => field.Id)
            .ToHashSet();

        if (ambiguous.Count > 0)
        {
            Add(
                "AMBIGUOUS_FIELD",
                "Multiple current values exist for the same document field.",
                ambiguous);
        }

        bool Reliable(ScreeningField field) =>
            !ambiguous.Contains(field.Id)
            && field.Confidence is { } confidence
            && double.IsFinite(confidence)
            && confidence >= options.MinimumExtractionConfidence
            && confidence <= 1;

        foreach (var field in fields)
        {
            if (field.Confidence is null
                || !double.IsFinite(field.Confidence.Value)
                || field.Confidence < options.MinimumExtractionConfidence
                || field.Confidence > 1)
            {
                Add(
                    "LOW_EXTRACTION_CONFIDENCE",
                    "A field has missing, low, or invalid extraction confidence.",
                    [field.Id]);
            }
            else if (!Normalization.Valid(field.Name, field.Value))
            {
                Add(
                    "INVALID_FIELD",
                    $"A {field.Name} value has an invalid format.",
                    [field.Id]);
            }
        }

        var usable = fields
            .Where(field =>
                Reliable(field)
                && Normalization.Valid(field.Name, field.Value))
            .ToArray();

        ScreeningField[] Values(string key) =>
            usable.Where(field => field.Name == key).ToArray();

        DocumentType[] requiredDocuments =
            input.ApplicantKind == ApplicantKind.Individual
                ? [
                    DocumentType.Application,
                    DocumentType.GovernmentId,
                    DocumentType.W9
                ]
                : [
                    DocumentType.Application,
                    DocumentType.W9,
                    DocumentType.FormationDocument,
                    DocumentType.BeneficialOwnership
                ];

        foreach (var required in requiredDocuments)
        {
            if (!input.Documents.Any(document => document.Type == required))
            {
                Add(
                    "MISSING_REQUIRED_DOCUMENT",
                    $"Required illustrative document is missing: {required}.",
                    details: new { expectedDocumentType = required.ToString() });
            }
        }

        foreach (var document in input.Documents.OrderBy(document => document.Id))
        {
            foreach (var key in RequiredFields(
                         input.ApplicantKind,
                         document.Type))
            {
                if (!usable.Any(field =>
                        field.DocumentId == document.Id && field.Name == key))
                {
                    Add(
                        "MISSING_REQUIRED_FIELD",
                        $"Document {document.Id} has no usable {key}.",
                        fields.Where(field =>
                                field.DocumentId == document.Id
                                && field.Name == key)
                            .Select(field => field.Id),
                        details: new
                        {
                            documentId = document.Id,
                            expectedField = key
                        });
                }
            }
        }

        void CompareNames(
            string first,
            string second,
            Guid[] sourceIds,
            string comparison)
        {
            if (_names.Equivalent(
                    first,
                    second,
                    input.ApplicantKind == ApplicantKind.Individual))
            {
                return;
            }

            var similarity = _names.Similarity(first, second);

            Add(
                "NAME_MISMATCH",
                "Names differ and require human review.",
                sourceIds,
                similarity >= options.NearNameThreshold ? 0.5 : 1,
                new
                {
                    comparison,
                    similarity,
                    algorithm = "Jaro-Winkler",
                    initialExpansion = "illustrative-v1"
                });
        }

        // Applicant reference values are preserved in the run input snapshot.
        foreach (var field in Values("full_name"))
        {
            CompareNames(
                input.ApplicantName,
                field.Value,
                [field.Id],
                "applicant-reference");
        }

        if (input.ApplicantKind == ApplicantKind.Individual
            && input.ApplicantDob is { } applicantDob)
        {
            foreach (var field in Values("date_of_birth"))
            {
                Normalization.TryDate(field.Value, out var extractedDob);

                if (applicantDob != extractedDob)
                {
                    Add(
                        "DOB_MISMATCH",
                        "Document DOB differs from the applicant reference.",
                        [field.Id],
                        details: new { reference = "ApplicantDob" });
                }
            }
        }

        // Cross-document comparisons apply to the primary applicant.
        // Owner names use a separate canonical key and are not mixed in.
        foreach (var key in new[]
                 {
                     "full_name",
                     "date_of_birth",
                     "address",
                     "tin_last4"
                 })
        {
            var values = Values(key);

            for (var left = 0; left < values.Length; left++)
            {
                for (var right = left + 1; right < values.Length; right++)
                {
                    var a = values[left];
                    var b = values[right];

                    if (a.DocumentId == b.DocumentId)
                    {
                        continue;
                    }

                    if (key == "full_name")
                    {
                        CompareNames(
                            a.Value,
                            b.Value,
                            [a.Id, b.Id],
                            "cross-document");

                        continue;
                    }

                    if (Normalization.Canonical(key, a.Value)
                        == Normalization.Canonical(key, b.Value))
                    {
                        continue;
                    }

                    var code = key switch
                    {
                        "date_of_birth" => "DOB_MISMATCH",
                        "address" => "ADDRESS_MISMATCH",
                        "tin_last4" => "TIN_MISMATCH",
                        _ => throw new InvalidOperationException()
                    };

                    Add(
                        code,
                        $"{key} differs across documents.",
                        [a.Id, b.Id]);
                }
            }
        }

        var governmentIds = input.Documents
            .Where(document => document.Type == DocumentType.GovernmentId)
            .Select(document => document.Id)
            .ToHashSet();

        foreach (var field in Values("expiry_date")
                     .Where(field => governmentIds.Contains(field.DocumentId)))
        {
            Normalization.TryDate(field.Value, out var expiry);

            // Illustrative policy: valid through its expiry date.
            if (expiry < input.EvaluationDate)
            {
                Add(
                    "DOCUMENT_EXPIRED",
                    "Government ID has expired.",
                    [field.Id]);
            }
        }

        foreach (var field in Values("date_of_birth"))
        {
            Normalization.TryDate(field.Value, out var dob);

            var age = input.EvaluationDate.Year - dob.Year;

            if (dob.AddYears(age) > input.EvaluationDate)
            {
                age--;
            }

            if (dob > input.EvaluationDate
                || age > options.MaximumPlausibleAge)
            {
                Add(
                    "IMPLAUSIBLE_AGE",
                    "DOB is in the future or exceeds the illustrative age limit.",
                    [field.Id]);
            }
        }

        foreach (var issue in Values("issue_date"))
        {
            Normalization.TryDate(issue.Value, out var issueDate);

            if (issueDate > input.EvaluationDate)
            {
                Add(
                    "INVALID_DATE_RANGE",
                    "Document issue date is in the future.",
                    [issue.Id]);
            }

            foreach (var expiry in Values("expiry_date")
                         .Where(field => field.DocumentId == issue.DocumentId))
            {
                Normalization.TryDate(expiry.Value, out var expiryDate);

                if (expiryDate < issueDate)
                {
                    Add(
                        "INVALID_DATE_RANGE",
                        "Document expiry precedes its issue date.",
                        [issue.Id, expiry.Id]);
                }
            }
        }

        foreach (var field in Values("country_code"))
        {
            if (options.HighRiskCountryCodes.Contains(
                    field.Value.Trim(),
                    StringComparer.OrdinalIgnoreCase))
            {
                Add(
                    "HIGH_RISK_JURISDICTION",
                    "Country appears in the illustrative configured list.",
                    [field.Id]);
            }
        }

        foreach (var field in Values("address"))
        {
            if (Normalization.IsPoBox(field.Value))
            {
                Add(
                    "PO_BOX_ADDRESS",
                    "Address contains a PO box indicator.",
                    [field.Id]);
            }

            if (options.RegisteredAgentAddresses.Any(address =>
                    Normalization.Address(address)
                    == Normalization.Address(field.Value)))
            {
                Add(
                    "REGISTERED_AGENT_ADDRESS",
                    "Address matches the illustrative registered-agent list.",
                    [field.Id]);
            }
        }

        var ownerNames = Values("beneficial_owner_name");

        if (input.ApplicantKind == ApplicantKind.Entity
            && ownerNames.Length == 0)
        {
            Add(
                "MISSING_BENEFICIAL_OWNER",
                "Entity has no usable identified beneficial-owner name.",
                details: new { expectedField = "beneficial_owner_name" });
        }

        foreach (var contact in input.SharedContacts
                     .OrderBy(contact => contact.SourceFieldId)
                     .ThenBy(contact => contact.OtherCaseId)
                     .ThenBy(contact => contact.OtherFieldId))
        {
            var field = usable.FirstOrDefault(candidate =>
                candidate.Id == contact.SourceFieldId);

            if (field is null || field.Name != contact.Kind)
            {
                continue;
            }

            Add(
                contact.Kind == "address" ? "SHARED_ADDRESS" : "SHARED_PHONE",
                "Contact value is also present in an unrelated case.",
                [field.Id],
                details: new
                {
                    relatedEvidenceCaseId = contact.OtherCaseId,
                    relatedEvidenceFieldId = contact.OtherFieldId
                });
        }

        var snapshotAge = snapshot.Date is { } snapshotDate
            ? input.EvaluationDate.DayNumber - snapshotDate.DayNumber
            : int.MaxValue;

        var sanctionsUsable =
            snapshot.IsAvailable
            && snapshot.Date is not null
            && snapshotAge >= 0
            && snapshotAge <= options.MaximumSnapshotAgeDays;

        if (!sanctionsUsable)
        {
            Add(
                "SANCTIONS_UNAVAILABLE",
                "Sanctions screening is incomplete: the snapshot is "
                + "missing, future-dated, or older than the configured limit.",
                risk: 0,
                details: new
                {
                    snapshot.Source,
                    snapshot.Date,
                    snapshot.Hash,
                    snapshot.IsFixture
                });
        }
        else
        {
            var targets = new List<(
                string Name,
                ApplicantKind Kind,
                Guid[] Fields,
                string Reference)>
            {
                (
                    input.ApplicantName,
                    input.ApplicantKind,
                    [],
                    "ApplicantName"
                )
            };

            targets.AddRange(Values("full_name").Select(field =>
                (
                    field.Value,
                    input.ApplicantKind,
                    new[] { field.Id },
                    "document-name"
                )));

            targets.AddRange(ownerNames.Select(field =>
                (
                    field.Value,
                    ApplicantKind.Individual,
                    new[] { field.Id },
                    "beneficial-owner"
                )));

            foreach (var target in targets)
            {
                foreach (var entry in snapshot.Entries
                             .Where(entry => entry.Kind == target.Kind)
                             .OrderBy(entry => entry.Id, StringComparer.Ordinal))
                {
                    var best = entry.Names
                        .Select(name => new
                        {
                            Name = name,
                            Similarity = _names.Similarity(target.Name, name)
                        })
                        .OrderByDescending(match => match.Similarity)
                        .ThenBy(match => match.Name, StringComparer.Ordinal)
                        .FirstOrDefault();

                    if (best is null
                        || best.Similarity < options.SanctionsThreshold)
                    {
                        continue;
                    }

                    Add(
                        "OFAC_POTENTIAL_MATCH",
                        "Potential sanctions name candidate; review additional "
                        + "identifiers before making a decision.",
                        target.Fields,
                        details: new
                        {
                            snapshot.Source,
                            snapshot.Date,
                            snapshot.Hash,
                            snapshot.IsFixture,
                            entryId = entry.Id,
                            entry.Programs,
                            matchedName = best.Name,
                            best.Similarity,
                            algorithm = "Jaro-Winkler",
                            screenedReference = target.Reference
                        });
                }
            }
        }

        var findings = pending.Values
            .OrderBy(finding => finding.Code, StringComparer.Ordinal)
            .Select(finding =>
            {
                var definition = options.Rules[finding.Code];

                return new RuleDraft(
                    finding.Code,
                    definition.Severity,
                    string.Join(" ", finding.Messages),
                    finding.Risk,
                    finding.Fields.OrderBy(id => id).ToArray(),
                    JsonSerializer.Serialize(new
                    {
                        illustrative = true,
                        rulesetVersion = options.RulesetVersion,
                        details = finding.Details
                    }));
            })
            .ToArray();

        var contributions = findings.Select(finding =>
        {
            var weight = options.Rules[finding.Code].Weight;

            return new ScoreContribution(
                finding.Code,
                weight,
                finding.Risk,
                weight * finding.Risk);
        }).ToArray();

        var total = contributions.Sum(contribution => contribution.Points);

        var tier = total >= options.HighThreshold
            ? "High"
            : total >= options.MediumThreshold
                ? "Medium"
                : "Low";

        return new EngineResult(
            sanctionsUsable,
            total,
            tier,
            findings,
            contributions);
    }

    private static string[] RequiredFields(
        ApplicantKind kind,
        DocumentType type) =>
        type switch
        {
            DocumentType.Application when kind == ApplicantKind.Individual =>
                ["full_name", "date_of_birth", "address"],

            DocumentType.Application =>
                ["full_name", "address"],

            DocumentType.GovernmentId =>
                ["full_name", "date_of_birth", "expiry_date"],

            DocumentType.W9 =>
                ["full_name", "address", "tin_last4"],

            DocumentType.ProofOfAddress =>
                ["full_name", "address"],

            DocumentType.FormationDocument =>
                ["full_name"],

            DocumentType.BeneficialOwnership =>
                ["beneficial_owner_name"],

            _ => []
        };
}