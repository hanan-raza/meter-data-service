namespace MeterDataService.Domain;

/// <summary>
/// Identifier of a market location (Marktlokations-ID, MaLo-ID): 11 digits, the last one
/// being a check digit. Validating the check digit at the boundary catches typos before they
/// turn into orphaned readings that nobody can bill.
/// </summary>
public static class MarketLocationId
{
    public const int Length = 11;

    public static bool IsValid(string? value)
    {
        if (value is null || value.Length != Length || value[0] == '0')
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return value[^1] - '0' == CalculateCheckDigit(value.AsSpan(0, Length - 1));
    }

    /// <summary>
    /// Digits at odd positions count once, digits at even positions count twice; the check digit
    /// tops the sum up to the next multiple of ten.
    /// </summary>
    public static int CalculateCheckDigit(ReadOnlySpan<char> firstTenDigits)
    {
        if (firstTenDigits.Length != Length - 1)
        {
            throw new ArgumentException($"Expected {Length - 1} digits.", nameof(firstTenDigits));
        }

        var sum = 0;
        for (var i = 0; i < firstTenDigits.Length; i++)
        {
            var digit = firstTenDigits[i] - '0';
            sum += i % 2 == 0 ? digit : digit * 2;
        }

        return (10 - sum % 10) % 10;
    }
}
