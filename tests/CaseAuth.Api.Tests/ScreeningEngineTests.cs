using System.Text;
using System.Text.Json;
using CaseAuth.Api.Entities;
using CaseAuth.Api.Screening;

namespace CaseAuth.Api.Tests;

public class ScreeningEngineTests
{
    private static readonly DateOnly Today = new(2026, 10, 2);

    private static readonly Guid ApplicationId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static readonly Guid IdDocumentId =
        Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static readonly Guid W9Id =
        Guid.Parse("00000000-0000-0000-0000-000000000003");

    private readonly ScreeningEngine _engine =
        new(new ScreeningOptions());

    private static ScreeningField Field(
        Guid documentId,
        string name,
        string value,
        double? confidence = 0.99) =>
        new(Guid.NewGuid(), documentId, name, value, confidence);

    private static ScreeningInput Input(
        string name = "John Adam",
        string idName = "John Adam",
        string address = "10 Main Street",
        string idAddress = "10 Main St",
        string expiry = "2030-01-01") =>
        new(
            Guid.NewGuid(),
            ApplicantKind.Individual,
            name,
            new DateOnly(1990, 4, 12),
            Today,
            [
                new(ApplicationId, DocumentType.Application),
                new(IdDocumentId, DocumentType.GovernmentId),
                new(W9Id, DocumentType.W9)
            ],
            [
                Field(ApplicationId, "full_name", name),
                Field(ApplicationId, "date_of_birth", "1990-04-12"),
                Field(ApplicationId, "address", address),

                Field(IdDocumentId, "full_name", idName),
                Field(IdDocumentId, "date_of_birth", "1990-04-12"),
                Field(IdDocumentId, "address", idAddress),
                Field(IdDocumentId, "expiry_date", expiry),

                Field(W9Id, "full_name", name),
                Field(W9Id, "address", address),
                Field(W9Id, "tin_last4", "0123")
            ],
            []);

    private EngineResult Run(
        ScreeningInput input,
        SanctionsSnapshot? snapshot = null) =>
        _engine.Evaluate(
            input,
            snapshot ?? SanctionsSnapshots.Fixture(Today));

    [Fact]
    public void Consistent_case_has_zero_findings()
    {
        var result = Run(Input());

        Assert.True(result.IsComplete);
        Assert.Empty(result.Findings);
        Assert.Equal(0d, result.TotalScore);
    }

    [Fact]
    public void Smith_Smyth_produces_name_flag()
    {
        var result = Run(Input(name: "Smith", idName: "Smyth"));

        var finding = Assert.Single(result.Findings.Where(
            finding => finding.Code == "NAME_MISMATCH"));

        Assert.True(finding.SourceFieldIds.Length >= 2);
    }

    [Fact]
    public void John_initial_and_expanded_name_pass()
    {
        var result = Run(Input(name: "John A.", idName: "John Adam"));

        Assert.DoesNotContain(
            result.Findings,
            finding => finding.Code == "NAME_MISMATCH");
    }

    [Fact]
    public void Address_mismatch_links_both_documents()
    {
        var input = Input(idAddress: "99 Other Road");
        var result = Run(input);

        var finding = Assert.Single(result.Findings.Where(
            finding => finding.Code == "ADDRESS_MISMATCH"));

        var documents = input.Fields
            .Where(field => finding.SourceFieldIds.Contains(field.Id))
            .Select(field => field.DocumentId)
            .Distinct()
            .ToArray();

        Assert.True(documents.Length >= 2);
    }

    [Fact]
    public void Tin_last_four_preserves_leading_zeroes()
    {
        var input = Input();

        input = input with
        {
            Fields =
            [
                .. input.Fields,
                Field(ApplicationId, "tin_last4", "1123")
            ]
        };

        Assert.Contains(
            Run(input).Findings,
            finding => finding.Code == "TIN_MISMATCH");
    }

    [Fact]
    public void Expired_id_is_flagged_but_today_is_still_valid()
    {
        Assert.Contains(
            Run(Input(expiry: "2026-10-01")).Findings,
            finding => finding.Code == "DOCUMENT_EXPIRED");

        Assert.DoesNotContain(
            Run(Input(expiry: "2026-10-02")).Findings,
            finding => finding.Code == "DOCUMENT_EXPIRED");
    }

    [Fact]
    public void Low_confidence_name_is_excluded_from_comparison()
    {
        var input = Input(idName: "Entirely Different");

        input = input with
        {
            Fields = input.Fields.Select(field =>
                field.DocumentId == IdDocumentId
                && field.Name == "full_name"
                    ? field with { Confidence = 0.2 }
                    : field).ToArray()
        };

        var result = Run(input);

        Assert.Contains(result.Findings,
            finding => finding.Code == "LOW_EXTRACTION_CONFIDENCE");

        Assert.DoesNotContain(result.Findings,
            finding => finding.Code == "NAME_MISMATCH");
    }

    [Fact]
    public void Missing_dob_is_not_reported_as_mismatching_dob()
    {
        var input = Input();

        input = input with
        {
            Fields = input.Fields.Where(field =>
                !(field.DocumentId == IdDocumentId
                  && field.Name == "date_of_birth")).ToArray()
        };

        var result = Run(input);

        Assert.Contains(result.Findings,
            finding => finding.Code == "MISSING_REQUIRED_FIELD");

        Assert.DoesNotContain(result.Findings,
            finding => finding.Code == "DOB_MISMATCH");
    }

    [Fact]
    public void Future_dob_is_flagged()
    {
        var input = Input();

        input = input with
        {
            Fields = input.Fields.Select(field =>
                field.Name == "date_of_birth"
                    ? field with { Value = "2030-01-01" }
                    : field).ToArray()
        };

        Assert.Contains(Run(input).Findings,
            finding => finding.Code == "IMPLAUSIBLE_AGE");
    }

    [Fact]
    public void Missing_required_document_is_flagged()
    {
        var input = Input() with
        {
            Documents =
            [
                new(ApplicationId, DocumentType.Application),
                new(IdDocumentId, DocumentType.GovernmentId)
            ]
        };

        Assert.Contains(Run(input).Findings,
            finding => finding.Code == "MISSING_REQUIRED_DOCUMENT");
    }

    [Fact]
    public void Sanctions_candidate_has_snapshot_provenance()
    {
        var result = Run(Input(
            name: "Demo Sanctioned Person",
            idName: "Demo Sanctioned Person"));

        var finding = Assert.Single(result.Findings.Where(
            finding => finding.Code == "OFAC_POTENTIAL_MATCH"));

        Assert.Contains("FIXTURE-001", finding.EvidenceJson);
        Assert.Contains("2026-10-02", finding.EvidenceJson);
        Assert.Equal("High", result.RiskTier);
    }

    [Fact]
    public void Unavailable_snapshot_makes_run_incomplete()
    {
        var snapshot = SanctionsSnapshots.Fixture(Today) with
        {
            IsAvailable = false
        };

        var result = Run(Input(), snapshot);

        Assert.False(result.IsComplete);
        Assert.Contains(result.Findings,
            finding => finding.Code == "SANCTIONS_UNAVAILABLE");
    }

    [Fact]
    public void Old_snapshot_makes_run_incomplete()
    {
        var snapshot = SanctionsSnapshots.Fixture(
            Today.AddDays(-60));

        Assert.False(Run(Input(), snapshot).IsComplete);
    }

    [Fact]
    public void Po_box_is_flagged()
    {
        var result = Run(Input(
            address: "P.O. Box 100",
            idAddress: "P.O. Box 100"));

        Assert.Contains(result.Findings,
            finding => finding.Code == "PO_BOX_ADDRESS");
    }

    [Fact]
    public void Configured_jurisdiction_and_agent_address_are_flagged()
    {
        var input = Input(
            address: "900 Demo Agent Way",
            idAddress: "900 Demo Agent Way");

        input = input with
        {
            Fields =
            [
                .. input.Fields,
                Field(ApplicationId, "country_code", "ZZ")
            ]
        };

        var result = Run(input);

        Assert.Contains(result.Findings,
            finding => finding.Code == "HIGH_RISK_JURISDICTION");

        Assert.Contains(result.Findings,
            finding => finding.Code == "REGISTERED_AGENT_ADDRESS");
    }

    [Fact]
    public void Entity_without_owner_is_flagged()
    {
        var input = Input() with
        {
            ApplicantKind = ApplicantKind.Entity
        };

        Assert.Contains(Run(input).Findings,
            finding => finding.Code == "MISSING_BENEFICIAL_OWNER");
    }

    [Fact]
    public void Shared_contact_preserves_own_field_evidence()
    {
        var input = Input();
        var source = input.Fields.First(field => field.Name == "address");

        input = input with
        {
            SharedContacts =
            [
                new SharedContact(
                    source.Id,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "address")
            ]
        };

        var finding = Assert.Single(Run(input).Findings.Where(
            finding => finding.Code == "SHARED_ADDRESS"));

        Assert.Contains(source.Id, finding.SourceFieldIds);
    }

    [Fact]
    public void Repeated_discrepancies_contribute_once_per_rule()
    {
        var result = Run(Input(idAddress: "99 Different Street"));

        Assert.Single(result.Contributions.Where(
            contribution => contribution.Code == "ADDRESS_MISMATCH"));

        Assert.Equal(
            result.Contributions.Sum(contribution => contribution.Points),
            result.TotalScore);
    }

    [Fact]
    public void Same_pinned_inputs_produce_same_result()
    {
        var input = Input(idAddress: "99 Other Road");

        Assert.Equal(
            JsonSerializer.Serialize(Run(input)),
            JsonSerializer.Serialize(Run(input)));
    }

    [Fact]
    public void Legacy_xml_loader_reads_aliases()
    {
        const string xml = """
            <sdnList xmlns="urn:demo">
              <sdnEntry>
                <uid>100</uid>
                <firstName>Primary</firstName>
                <lastName>Person</lastName>
                <sdnType>Individual</sdnType>
                <programList><program>DEMO</program></programList>
                <akaList>
                  <aka>
                    <uid>101</uid>
                    <firstName>Alias</firstName>
                    <lastName>Person</lastName>
                  </aka>
                </akaList>
              </sdnEntry>
            </sdnList>
            """;

        var snapshot = SanctionsSnapshots.ReadLegacyXml(
            Encoding.UTF8.GetBytes(xml),
            Today);

        var entry = Assert.Single(snapshot.Entries);

        Assert.Contains("Alias Person", entry.Names);
        Assert.False(snapshot.IsFixture);
        Assert.Equal(64, snapshot.Hash.Length);
    }
}