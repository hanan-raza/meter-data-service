namespace MeterDataService.Domain;

/// <summary>
/// Identifier of a meter location (Messlokations-ID / Zählpunktbezeichnung): 33 characters,
/// starting with a two-letter country code followed by upper-case letters and digits.
/// </summary>
public static class MeterLocationId
{
    public const int Length = 33;

    public static bool IsValid(string? value)
    {
        if (value is null || value.Length != Length)
        {
            return false;
        }

        if (!char.IsAsciiLetterUpper(value[0]) || !char.IsAsciiLetterUpper(value[1]))
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiDigit(c) && !char.IsAsciiLetterUpper(c))
            {
                return false;
            }
        }

        return true;
    }
}
