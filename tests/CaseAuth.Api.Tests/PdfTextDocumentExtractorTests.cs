using CaseAuth.Api.Entities;
using CaseAuth.Api.Pipeline;
using CaseAuth.Api.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace CaseAuth.Api.Tests;

public class PdfTextDocumentExtractorTests
{
    private sealed class InMemoryStorage(byte[] content) : IFileStorageService
    {
        public Task<string> SaveAsync(string firmId, Guid caseId, string fileName, Stream c, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct) =>
            Task.FromResult<Stream>(new MemoryStream(content));
    }

    private static Document Doc(string contentType = "application/pdf") => new()
    {
        FileName = "test.pdf",
        ContentType = contentType,
        StorageKey = "FIRM-A/x/test.pdf",
        UploadedByUserId = "u-analyst1",
    };

    // Label and value as separate text runs on one baseline - the way most generated forms
    // (and the synthetic test documents) lay them out.
    private static byte[] Pdf(params (string Label, string Value)[] rows)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.Letter);
        var regular = builder.AddStandard14Font(Standard14Font.Helvetica);
        var bold = builder.AddStandard14Font(Standard14Font.HelveticaBold);

        page.AddText("SYNTHETIC TEST DOCUMENT - NOT VALID", 8, new PdfPoint(54, 740), regular);
        double y = 700;
        foreach (var (label, value) in rows)
        {
            page.AddText(label + ":", 11, new PdfPoint(54, y), bold);
            page.AddText(value, 11, new PdfPoint(204, y), regular);
            y -= 24;
        }

        return builder.Build();
    }

    private static Task<IReadOnlyList<ExtractedFieldResult>> Extract(byte[] content, Document? document = null) =>
        new PdfTextDocumentExtractor(new InMemoryStorage(content), NullLogger<PdfTextDocumentExtractor>.Instance)
            .ExtractAsync(document ?? Doc(), CancellationToken.None);

    [Fact]
    public async Task ReadsKnownLabels_IntoScreeningFieldKeys()
    {
        var results = await Extract(Pdf(
            ("Name", "Eleanor Vance"),
            ("Date of birth", "1899-03-14"),
            ("Address", "300 Birch Drive Apt 1001, Madison, WI 53703"),
            ("TIN (last 4)", "48O2"),
            ("Expiry date", "2024-06-30"),
            ("Favourite colour", "blue")));

        Assert.Equal(
            new[]
            {
                ("full_name", "Eleanor Vance"),
                ("date_of_birth", "1899-03-14"),
                ("address", "300 Birch Drive Apt 1001, Madison, WI 53703"),
                ("tin_last4", "48O2"),
                ("expiry_date", "2024-06-30"),
            },
            results.Select(r => (r.FieldName, r.FieldValue)));
        Assert.All(results, r => Assert.Equal(PdfTextDocumentExtractor.TextLayerConfidence, r.Confidence));
    }

    [Fact]
    public async Task RepeatedLabel_KeepsFirstValueOnly()
    {
        var results = await Extract(Pdf(("Name", "Maria Lopez"), ("Full name", "Someone Else")));

        Assert.Equal("Maria Lopez", Assert.Single(results).FieldValue);
    }

    [Fact]
    public async Task ImageUpload_ExtractsNothing()
    {
        Assert.Empty(await Extract(Pdf(("Name", "Maria Lopez")), Doc("image/png")));
    }

    [Fact]
    public async Task CorruptPdf_ExtractsNothingInsteadOfThrowing()
    {
        Assert.Empty(await Extract("%PDF-1.4 fixture"u8.ToArray()));
    }
}
