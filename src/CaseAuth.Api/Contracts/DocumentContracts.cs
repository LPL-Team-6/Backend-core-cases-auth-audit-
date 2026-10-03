using System.ComponentModel.DataAnnotations;
using CaseAuth.Api.Entities;
using CaseAuth.Api.Services;

namespace CaseAuth.Api.Contracts;

public record DocumentResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    DocumentType DocumentType,
    string UploadedByUserId,
    DateTime UploadedAt)
{
    public static DocumentResponse From(Document d) => new(
        d.Id, d.FileName, d.ContentType, d.SizeBytes, d.DocumentType, d.UploadedByUserId, d.UploadedAt);
}

public record ExtractedFieldInput(
    [Required, MaxLength(200)] string FieldName,
    [Required, MaxLength(2000)] string FieldValue,
    double? Confidence);

public record CreateExtractedFieldsRequest([Required, MinLength(1)] List<ExtractedFieldInput> Fields);

// FieldValue is masked to the last four for tax IDs (IsMasked = true); see Services/SensitiveFields.
public record ExtractedFieldResponse(Guid Id, string FieldName, string FieldValue, bool IsMasked, double? Confidence, DateTime ExtractedAt)
{
    public static ExtractedFieldResponse From(ExtractedField f) => new(
        f.Id, f.FieldName, SensitiveFields.Mask(f.FieldName, f.FieldValue), SensitiveFields.IsSensitive(f.FieldName),
        f.Confidence, f.ExtractedAt);
}

public record RevealedFieldResponse(Guid Id, string FieldName, string FieldValue);
