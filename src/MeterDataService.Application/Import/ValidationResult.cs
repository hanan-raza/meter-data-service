namespace MeterDataService.Application.Import;

/// <summary>Severity of a plausibility check (Plausibilisierung).</summary>
public enum ValidationOutcome
{
    Ok = 0,

    /// <summary>Value or period is usable but needs attention, e.g. missing intervals.</summary>
    Warning = 1,

    /// <summary>Value is implausible and must not be used; it becomes a candidate for gap filling.</summary>
    Rejected = 2,
}

/// <summary>Plausibility rule that produced a result.</summary>
public enum ValidationRule
{
    OutsidePeriod = 1,
    DuplicateInterval = 2,
    NegativeValue = 3,
    Spike = 4,
    MissingInterval = 5,
    DstIntervalCount = 6,
}

/// <summary>Result of validating a single value; <see cref="Reason"/> says why, in plain language.</summary>
public sealed record ValidationResult(ValidationOutcome Outcome, ValidationRule? Rule, string? Reason)
{
    public static ValidationResult Ok { get; } = new(ValidationOutcome.Ok, null, null);

    public static ValidationResult Rejected(ValidationRule rule, string reason) => new(ValidationOutcome.Rejected, rule, reason);
}

public sealed record ValidatedReading(ImportedReading Reading, ValidationResult Result);

/// <summary>Finding about the period rather than a single value, e.g. a gap or a wrong interval count on a DST day.</summary>
/// <param name="From">Inclusive start of the affected range, in UTC.</param>
/// <param name="To">Exclusive end of the affected range, in UTC.</param>
public sealed record ValidationFinding(
    ValidationRule Rule, ValidationOutcome Outcome, DateTimeOffset From, DateTimeOffset To, string Reason);

/// <param name="Values">One entry per input reading, in input order.</param>
/// <param name="MissingIntervals">UTC starts of all intervals in the period without any reading.</param>
/// <param name="Findings">Period-level findings.</param>
public sealed record ValidationReport(
    IReadOnlyList<ValidatedReading> Values,
    IReadOnlyList<DateTimeOffset> MissingIntervals,
    IReadOnlyList<ValidationFinding> Findings);
