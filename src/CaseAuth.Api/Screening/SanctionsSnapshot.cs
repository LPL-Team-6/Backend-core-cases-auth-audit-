using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CaseAuth.Api.Entities;

namespace CaseAuth.Api.Screening;

public sealed record SanctionsEntry(
    string Id,
    ApplicantKind Kind,
    string[] Names,
    string[] Programs);

public sealed record SanctionsSnapshot(
    string Source,
    DateOnly? Date,
    string Hash,
    bool IsFixture,
    bool IsAvailable,
    SanctionsEntry[] Entries);

public static class SanctionsSnapshots
{
    public static SanctionsSnapshot Fixture(DateOnly date)
    {
        var entries = new[]
        {
            new SanctionsEntry(
                "FIXTURE-001",
                ApplicantKind.Individual,
                ["DEMO SANCTIONED PERSON"],
                ["SYNTHETIC"]),

            new SanctionsEntry(
                "FIXTURE-002",
                ApplicantKind.Entity,
                ["DEMO BLOCKED COMPANY"],
                ["SYNTHETIC"])
        };

        var bytes = Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(entries));

        return new SanctionsSnapshot(
            "Synthetic demo fixture — not OFAC data",
            date,
            Convert.ToHexString(SHA256.HashData(bytes)),
            true,
            true,
            entries);
    }

    public static SanctionsSnapshot FromConfiguration(
        ScreeningOptions options,
        TimeProvider clock)
    {
        if (options.SanctionsMode == "Fixture")
        {
            return Fixture(
                DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        }

        if (options.SanctionsMode != "OfacXml")
        {
            throw new InvalidOperationException(
                "SanctionsMode must be Fixture or OfacXml.");
        }

        if (!DateOnly.TryParseExact(
                options.OfacSnapshotDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            throw new InvalidOperationException(
                "OfacSnapshotDate must contain the real snapshot's "
                + "publication/as-of date in yyyy-MM-dd format.");
        }

        if (string.IsNullOrWhiteSpace(options.OfacXmlPath)
            || !File.Exists(options.OfacXmlPath))
        {
            return new SanctionsSnapshot(
                "OFAC SDN XML unavailable",
                date,
                "",
                false,
                false,
                []);
        }

        return ReadLegacyXml(
            File.ReadAllBytes(options.OfacXmlPath),
            date);
    }

    public static SanctionsSnapshot ReadLegacyXml(
        byte[] bytes,
        DateOnly date)
    {
        using var stream = new MemoryStream(bytes);

        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 100_000_000
        });

        var document = XDocument.Load(reader);

        static string Child(XElement element, string name) =>
            element.Elements()
                .FirstOrDefault(child => child.Name.LocalName == name)
                ?.Value.Trim() ?? "";

        static string Name(XElement element) =>
            string.Join(
                " ",
                new[]
                {
                    Child(element, "firstName"),
                    Child(element, "lastName")
                }.Where(part => part.Length > 0));

        var entries = new List<SanctionsEntry>();

        foreach (var node in document.Descendants()
                     .Where(element => element.Name.LocalName == "sdnEntry"))
        {
            var kind = Child(node, "sdnType") switch
            {
                "Individual" => (ApplicantKind?)ApplicantKind.Individual,
                "Entity" => ApplicantKind.Entity,
                _ => null
            };

            // This module screens people and entities.
            if (kind is null)
            {
                continue;
            }

            var id = Child(node, "uid");

            var names = new[] { Name(node) }
                .Concat(node.Descendants()
                    .Where(element => element.Name.LocalName == "aka")
                    .Select(Name))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            var programs = node.Descendants()
                .Where(element => element.Name.LocalName == "program")
                .Select(element => element.Value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            if (id.Length == 0 || names.Length == 0)
            {
                throw new InvalidDataException(
                    "OFAC entry has no identifier or usable name.");
            }

            entries.Add(new SanctionsEntry(id, kind.Value, names, programs));
        }

        if (entries.Count == 0
            || entries.Select(entry => entry.Id).Distinct().Count()
                != entries.Count)
        {
            throw new InvalidDataException(
                "Expected nonempty legacy sdn.xml with unique entry IDs.");
        }

        return new SanctionsSnapshot(
            "OFAC SDN legacy XML",
            date,
            Convert.ToHexString(SHA256.HashData(bytes)),
            false,
            true,
            entries.OrderBy(entry => entry.Id, StringComparer.Ordinal)
                .ToArray());
    }
}