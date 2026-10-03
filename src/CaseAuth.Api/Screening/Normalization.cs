using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using F23.StringSimilarity;

namespace CaseAuth.Api.Screening;

public static class Normalization
{
    public static string Text(string value)
    {
        var result = new StringBuilder();

        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            result.Append(
                char.IsLetterOrDigit(character)
                    ? char.ToUpperInvariant(character)
                    : ' ');
        }

        return Regex.Replace(result.ToString(), @"\s+", " ").Trim();
    }

    public static string Key(string value)
    {
        var key = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]", "");

        return key switch
        {
            "name" or "fullname" => "full_name",
            "dob" or "dateofbirth" => "date_of_birth",
            "address" or "residentialaddress" => "address",
            "tinlast4" or "ssnlast4" => "tin_last4",
            "expirydate" or "expirationdate" => "expiry_date",
            "issuedate" => "issue_date",
            "country" or "countrycode" => "country_code",
            "phone" or "phonenumber" => "phone",
            "beneficialownername" => "beneficial_owner_name",
            _ => value.Trim().ToLowerInvariant()
        };
    }

    public static bool TryDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

    public static string Address(string value)
    {
        var replacements = new Dictionary<string, string>
        {
            ["STREET"] = "ST",
            ["AVENUE"] = "AVE",
            ["ROAD"] = "RD",
            ["BOULEVARD"] = "BLVD",
            ["DRIVE"] = "DR",
            ["LANE"] = "LN"
        };

        return string.Join(
            " ",
            Text(value).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(token =>
                    replacements.TryGetValue(token, out var replacement)
                        ? replacement
                        : token));
    }

    public static string Phone(string value) =>
        string.Concat(value.Where(character =>
            character is >= '0' and <= '9'));

    public static bool IsPoBox(string value) =>
        Regex.IsMatch(
            Text(value),
            @"\b(?:P\s*O|POST OFFICE)\s+BOX\b");

    public static bool Valid(string key, string value) =>
        key switch
        {
            "date_of_birth" or "expiry_date" or "issue_date" =>
                TryDate(value, out _),

            "tin_last4" =>
                Regex.IsMatch(value, @"^[0-9]{4}$"),

            "country_code" =>
                Regex.IsMatch(value.Trim(), @"^[A-Za-z]{2}$"),

            "phone" =>
                Phone(value).Length is >= 7 and <= 15,

            _ => Text(value).Length > 0
        };

    public static string Canonical(string key, string value) =>
        key switch
        {
            "address" => Address(value),
            "phone" => Phone(value),
            "tin_last4" => value,
            "date_of_birth" or "expiry_date" or "issue_date" =>
                TryDate(value, out var date)
                    ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : "",
            _ => Text(value)
        };
}

public sealed class NameMatcher
{
    private readonly JaroWinkler _jaroWinkler = new();

    public double Similarity(string first, string second)
    {
        var left = Normalization.Text(first);
        var right = Normalization.Text(second);

        return left.Length == 0 || right.Length == 0
            ? 0
            : _jaroWinkler.Similarity(left, right);
    }

    public bool Equivalent(
        string first,
        string second,
        bool allowInitialExpansion)
    {
        var left = Normalization.Text(first);
        var right = Normalization.Text(second);

        if (left.Length == 0 || right.Length == 0)
        {
            return false;
        }

        if (left == right)
        {
            return true;
        }

        if (!allowInitialExpansion)
        {
            return false;
        }

        var a = left.Split(' ');
        var b = right.Split(' ');

        if (a.Length < 2 || a.Length != b.Length || a[0] != b[0])
        {
            return false;
        }

        for (var index = 1; index < a.Length; index++)
        {
            if (a[index] == b[index])
            {
                continue;
            }

            if (a[index][0] != b[index][0]
                || (a[index].Length != 1 && b[index].Length != 1))
            {
                return false;
            }
        }

        return true;
    }
}