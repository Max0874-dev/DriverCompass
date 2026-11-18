namespace DriverCompass.Core.Evaluation;

/// <summary>
/// Detects the placeholder strings that white-box PCs report in SMBIOS
/// ("To Be Filled By O.E.M.", "System manufacturer", "Default string" …).
/// </summary>
public static class SystemInfoSanitizer
{
    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "To Be Filled By O.E.M.",
        "To Be Filled By OEM",
        "System manufacturer",
        "System Manufacturer",
        "System Product Name",
        "System Version",
        "System Serial Number",
        "System SKU",
        "SKU",
        "Default string",
        "Default String",
        "O.E.M.",
        "OEM",
        "Not Applicable",
        "Not Specified",
        "Not Available",
        "None",
        "N/A",
        "NA",
        "Invalid",
        "Unknown",
        "0123456789",
        "123456789",
        "1234567890",
        "Base Board Serial Number",
        "Chassis Serial Number",
        "Type1ProductConfigId",
        "Type2 - Board Serial Number",
        "All Series",
        "Standard PC",
    };

    public static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();
        if (Placeholders.Contains(trimmed))
        {
            return true;
        }

        // "00000000", "XXXXXXXX", "........"
        var repeatedSymbol = trimmed.Length > 1 && !char.IsLetter(trimmed[0]) && trimmed.All(c => c == trimmed[0]);
        var onlyXOrDash = trimmed.Length > 1 && trimmed.All(c => c is 'x' or 'X' or '-');
        return repeatedSymbol || onlyXOrDash;
    }

    /// <summary>Returns the trimmed value or <c>null</c> for placeholders.</summary>
    public static string? Clean(string? value) => IsPlaceholder(value) ? null : value!.Trim();
}
