using System.Globalization;

namespace DriverCompass.Core.Versions;

/// <summary>
/// Driver version "a.b.c.d" as used in INF files (each part 0–65535 in practice, up to int here).
/// Missing parts count as 0, so "1.2" equals "1.2.0.0". Trailing markers such as the Lenovo
/// "≥" suffix <c>^</c> or text like " (A03)" are ignored.
/// </summary>
public readonly record struct DriverVersion(int Major, int Minor, int Build, int Revision) : IComparable<DriverVersion>
{
    public static bool TryParse(string? text, out DriverVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.AsSpan().Trim();
        var parts = new int[4];
        var count = 0;
        var i = 0;

        while (i < span.Length && count < 4)
        {
            var start = i;
            while (i < span.Length && char.IsAsciiDigit(span[i]))
            {
                i++;
            }

            if (i == start || !int.TryParse(span[start..i], NumberStyles.None, CultureInfo.InvariantCulture, out parts[count]))
            {
                return false;
            }

            count++;
            if (i < span.Length && span[i] == '.' && count < 4)
            {
                i++;
                continue;
            }

            break;
        }

        // Whatever follows the numeric part must not start with another digit group (e.g. "1.2.3.4.5").
        if (count == 0 || (i < span.Length && span[i] == '.' && i + 1 < span.Length && char.IsAsciiDigit(span[i + 1])))
        {
            return false;
        }

        version = new DriverVersion(parts[0], parts[1], parts[2], parts[3]);
        return true;
    }

    public int CompareTo(DriverVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0)
        {
            return c;
        }

        c = Minor.CompareTo(other.Minor);
        if (c != 0)
        {
            return c;
        }

        c = Build.CompareTo(other.Build);
        return c != 0 ? c : Revision.CompareTo(other.Revision);
    }

    public static bool operator <(DriverVersion a, DriverVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(DriverVersion a, DriverVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(DriverVersion a, DriverVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(DriverVersion a, DriverVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Build}.{Revision}");
}
