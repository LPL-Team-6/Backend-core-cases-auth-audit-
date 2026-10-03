namespace CaseAuth.Api.Services;

// Tax IDs are returned as last four only (UX brief R13). The full value is available one field at
// a time through the audited reveal endpoint on ExtractedFieldsController, and is never sent to the
// AI reviewer: no rule it explains needs more than the last four.
public static class SensitiveFields
{
    private static readonly HashSet<string> TaxIdNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "TIN", "SSN", "EIN", "ITIN", "TaxId", "TAX_ID",
    };

    public static bool IsSensitive(string fieldName) => TaxIdNames.Contains(fieldName.Trim());

    public static string Mask(string fieldName, string value)
    {
        if (!IsSensitive(fieldName))
        {
            return value;
        }

        var digits = new string(value.Where(char.IsLetterOrDigit).ToArray());
        var lastFour = digits.Length > 4 ? digits[^4..] : "";
        return $"•••••{lastFour}";
    }
}
