using System.Text.Json;
using System.Text.Json.Serialization;
using CaseAuth.Api.Entities;

namespace CaseAuth.Api.Demo;

// The shape of demo/personas.json - the same file demo/seed.mjs loads over HTTP and that the
// other modules use as test fixtures. Everything in it is synthetic. There are no findings in it:
// the rules engine produces them from the fields, and each AI review may only cite what it raised.
public record PersonaFile(List<Persona> Personas);

public record Persona(
    string Key,
    string Label,
    PersonaApplicant Applicant,
    List<PersonaDocument> Documents,
    PersonaAiReview AiReview);

public record PersonaApplicant(
    string FullName, DateOnly? DateOfBirth, string? Email, string? Phone, ApplicantKind Kind = ApplicantKind.Individual);

public record PersonaDocument(string Key, DocumentType Type, string Title, string File, List<PersonaField> Fields);

public record PersonaField(string Name, string Value, double? Confidence);

public record PersonaAiReview(
    AiRecommendation Recommendation,
    string Summary,
    List<PersonaConcern> KeyConcerns,
    List<string> NextSteps,
    string DraftCaseNote);

public record PersonaConcern(string Text, List<string> FindingCodes);

public static class PersonaLoader
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<List<Persona>> LoadAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var file = await JsonSerializer.DeserializeAsync<PersonaFile>(stream, JsonOptions, ct)
            ?? throw new InvalidOperationException($"'{path}' is empty.");
        return file.Personas;
    }
}
