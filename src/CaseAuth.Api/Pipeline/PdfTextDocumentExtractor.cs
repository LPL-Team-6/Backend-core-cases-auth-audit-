using System.Text.RegularExpressions;
using CaseAuth.Api.Entities;
using CaseAuth.Api.Storage;
using UglyToad.PdfPig;

namespace CaseAuth.Api.Pipeline;

// Local stand-in for the planned Textract extractor: reads the text layer of a PDF and picks out
// "Label: value" lines whose label is one the screening engine knows (see FieldKeys). Works for
// digitally generated forms - including the synthetic test documents - but not for scans or
// photos, which have no text layer; PNG/JPEG uploads and scanned PDFs yield no fields, the same
// as the old FixtureDocumentExtractor. Replace with TextractExtractor (AnalyzeID / AnalyzeDocument
// Queries) when that exists.
public partial class PdfTextDocumentExtractor(
    IFileStorageService storage,
    ILogger<PdfTextDocumentExtractor> logger) : IDocumentExtractor
{
    // The text layer is exact, unlike OCR, but the label match is heuristic - so high, not 1.0.
    public const double TextLayerConfidence = 0.95;

    // Normalized label (lowercase letters/digits only) -> screening field key.
    private static readonly Dictionary<string, string> FieldKeys = new()
    {
        ["name"] = "full_name",
        ["fullname"] = "full_name",
        ["legalname"] = "full_name",
        ["dateofbirth"] = "date_of_birth",
        ["dob"] = "date_of_birth",
        ["address"] = "address",
        ["residentialaddress"] = "address",
        ["phone"] = "phone",
        ["phonenumber"] = "phone",
        ["tinlast4"] = "tin_last4",
        ["ssnlast4"] = "tin_last4",
        ["issuedate"] = "issue_date",
        ["expirydate"] = "expiry_date",
        ["expirationdate"] = "expiry_date",
        ["country"] = "country_code",
        ["countrycode"] = "country_code",
        ["beneficialowner"] = "beneficial_owner_name",
        ["beneficialownername"] = "beneficial_owner_name",
    };

    public async Task<IReadOnlyList<ExtractedFieldResult>> ExtractAsync(Document document, CancellationToken ct)
    {
        if (!string.Equals(document.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Document {DocumentId} is {ContentType}; no OCR available, extracting nothing.",
                document.Id, document.ContentType);
            return [];
        }

        try
        {
            using var buffer = new MemoryStream();
            await using (var file = await storage.OpenReadAsync(document.StorageKey, ct))
            {
                await file.CopyToAsync(buffer, ct);
            }

            using var pdf = PdfDocument.Open(buffer.ToArray());
            var results = new List<ExtractedFieldResult>();
            var seen = new HashSet<string>();

            foreach (var line in pdf.GetPages().SelectMany(ReadLines))
            {
                var colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                var label = NonAlphanumeric().Replace(line[..colon].ToLowerInvariant(), "");
                var value = line[(colon + 1)..].Trim();

                // First occurrence wins: a repeated label would otherwise show up in screening as
                // AMBIGUOUS_FIELD for what is really one value printed twice.
                if (value.Length > 0 && FieldKeys.TryGetValue(label, out var key) && seen.Add(key))
                {
                    results.Add(new ExtractedFieldResult(key, value, TextLayerConfidence));
                }
            }

            logger.LogInformation("Extracted {Count} field(s) from document {DocumentId}.", results.Count, document.Id);
            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A corrupt or unreadable PDF shouldn't fail the whole Extract job - screening will
            // report the document's required fields as missing, which is the honest outcome.
            logger.LogWarning(ex, "Could not read text from document {DocumentId}; extracting nothing.", document.Id);
            return [];
        }
    }

    // PdfPig gives words with positions, not lines - group words sharing a baseline (within 2pt),
    // top to bottom, each line left to right.
    private static IEnumerable<string> ReadLines(UglyToad.PdfPig.Content.Page page) =>
        page.GetWords()
            .GroupBy(word => Math.Round(word.BoundingBox.Bottom / 2))
            .OrderByDescending(group => group.Key)
            .Select(group => string.Join(" ", group.OrderBy(word => word.BoundingBox.Left).Select(word => word.Text)));

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NonAlphanumeric();
}
