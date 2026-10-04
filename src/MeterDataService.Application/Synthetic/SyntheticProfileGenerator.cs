using MeterDataService.Domain;

namespace MeterDataService.Application.Synthetic;

/// <summary>
/// Generates realistic 15-minute series for demos and tests: a household standard load profile
/// (Standardlastprofil H0), a flat commercial base load, and PV feed-in (Einspeisung). Days are
/// German calendar days (Europe/Berlin), so a requested range contains 92 intervals on the
/// spring-forward day and 100 on the fall-back day.
/// </summary>
/// <remarks>
/// Gaps are emitted as missing intervals and spikes as plain <see cref="MeasurementStatus.Measured"/>
/// values — exactly how defects arrive from the field, so validation has to find them.
/// Shape math runs in <see cref="double"/>; each value is rounded once to the persisted scale.
/// </remarks>
public sealed class SyntheticProfileGenerator
{
    private const double MinSpikeFactor = 4;
    private const double MaxSpikeFactor = 8;
    private const int QuarterHoursPerYear = 365 * 96;

    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    // Hourly household shapes (local time) loosely following the published H0 profile:
    // low night load, morning ramp, lunch peak, pronounced evening peak.
    private static readonly double[] HouseholdWeekday =
        [70, 55, 48, 45, 45, 50, 75, 105, 110, 105, 105, 115, 130, 120, 105, 95, 100, 125, 155, 165, 150, 130, 110, 90];

    private static readonly double[] HouseholdSaturday =
        [80, 62, 52, 48, 46, 48, 58, 80, 110, 130, 135, 140, 150, 135, 120, 110, 112, 130, 155, 160, 150, 135, 118, 98];

    private static readonly double[] HouseholdSunday =
        [85, 66, 55, 50, 47, 47, 52, 68, 100, 130, 145, 160, 175, 150, 120, 108, 110, 128, 150, 158, 148, 130, 110, 90];

    private static readonly double HouseholdWeeklyMean =
        (5 * HouseholdWeekday.Average() + HouseholdSaturday.Average() + HouseholdSunday.Average()) / 7;

    private static readonly double DynamizationAnnualMean =
        Enumerable.Range(1, 365).Average(HouseholdDynamization);

    private readonly SyntheticProfileOptions _options;

    public SyntheticProfileGenerator(SyntheticProfileOptions? options = null)
    {
        _options = options ?? new SyntheticProfileOptions();
        EnsureProbability(_options.GapProbability, nameof(SyntheticProfileOptions.GapProbability));
        EnsureProbability(_options.SpikeProbability, nameof(SyntheticProfileOptions.SpikeProbability));
    }

    /// <summary>Household consumption scaled to the given annual energy (Jahresverbrauch).</summary>
    public IEnumerable<MeasurementValue> GenerateHousehold(
        Guid seriesId, DateOnly firstDay, DateOnly lastDay, decimal annualConsumptionKwh = 3_500m)
    {
        EnsurePositive(annualConsumptionKwh, nameof(annualConsumptionKwh));
        var meanIntervalKwh = (double)annualConsumptionKwh / QuarterHoursPerYear;

        return Generate(seriesId, firstDay, lastDay, rng => utc =>
        {
            var local = TimeZoneInfo.ConvertTime(utc, Berlin);
            var shape = local.DayOfWeek switch
            {
                DayOfWeek.Saturday => HouseholdSaturday,
                DayOfWeek.Sunday => HouseholdSunday,
                _ => HouseholdWeekday,
            };
            var seasonal = HouseholdDynamization(local.DayOfYear) / DynamizationAnnualMean;
            return meanIntervalKwh * shape[local.Hour] / HouseholdWeeklyMean * seasonal * Noise(rng, 0.10);
        });
    }

    /// <summary>Commercial site with a flat base load, e.g. cold storage or a data room (Bandlast).</summary>
    public IEnumerable<MeasurementValue> GenerateCommercial(
        Guid seriesId, DateOnly firstDay, DateOnly lastDay, decimal averageLoadKw = 20m)
    {
        EnsurePositive(averageLoadKw, nameof(averageLoadKw));
        var intervalKwh = (double)averageLoadKw * MeasurementSeries.IntervalLength.TotalHours;

        return Generate(seriesId, firstDay, lastDay, rng => _ => intervalKwh * Noise(rng, 0.05));
    }

    /// <summary>Photovoltaic feed-in (PV-Einspeisung) for a plant of the given peak power (kWp) in central Germany.</summary>
    public IEnumerable<MeasurementValue> GeneratePhotovoltaic(
        Guid seriesId, DateOnly firstDay, DateOnly lastDay, decimal peakPowerKwp = 10m)
    {
        EnsurePositive(peakPowerKwp, nameof(peakPowerKwp));
        const double performanceRatio = 0.85;
        // The sun follows UTC, not the clock: using local time here would shift production by an hour at every DST switch.
        const double solarNoonUtcHours = 11.3;
        var halfInterval = MeasurementSeries.IntervalLength / 2;

        return Generate(seriesId, firstDay, lastDay, rng =>
        {
            var cloudDay = DateOnly.MinValue;
            var cloudFactor = 1.0;

            return utc =>
            {
                var day = DateOnly.FromDateTime(utc.UtcDateTime);
                if (day != cloudDay)
                {
                    cloudDay = day;
                    cloudFactor = 0.25 + 0.75 * rng.NextDouble();
                }

                var season = Math.Sin(2 * Math.PI * (utc.DayOfYear - 80) / 365.0);
                var dayLengthHours = 12 + 4.2 * season;
                var sunrise = solarNoonUtcHours - dayLengthHours / 2;
                var mid = utc + halfInterval;
                var sinceSunrise = mid.TimeOfDay.TotalHours - sunrise;
                if (sinceSunrise <= 0 || sinceSunrise >= dayLengthHours)
                {
                    return 0;
                }

                var elevation = Math.Sin(Math.PI * sinceSunrise / dayLengthHours);
                var seasonalPeak = 0.65 + 0.35 * season;
                var powerKw = (double)peakPowerKwp * performanceRatio * seasonalPeak * Math.Pow(elevation, 1.5) * cloudFactor;
                return powerKw * MeasurementSeries.IntervalLength.TotalHours * Noise(rng, 0.05);
            };
        });
    }

    private IEnumerable<MeasurementValue> Generate(
        Guid seriesId, DateOnly firstDay, DateOnly lastDay, Func<Random, Func<DateTimeOffset, double>> profile)
    {
        if (lastDay < firstDay)
        {
            throw new ArgumentException($"Last day {lastDay:O} is before first day {firstDay:O}.", nameof(lastDay));
        }

        return Iterate();

        IEnumerable<MeasurementValue> Iterate()
        {
            // Created per enumeration so a seeded series is identical every time it is enumerated.
            var rng = _options.Seed is { } seed ? new Random(seed) : new Random();
            var energyAt = profile(rng);
            var end = LocalMidnightUtc(lastDay.AddDays(1));

            for (var utc = LocalMidnightUtc(firstDay); utc < end; utc += MeasurementSeries.IntervalLength)
            {
                // Always draw all three rolls so changing a probability doesn't reshuffle the underlying profile.
                var isGap = rng.NextDouble() < _options.GapProbability;
                var isSpike = rng.NextDouble() < _options.SpikeProbability;
                var spikeFactor = MinSpikeFactor + (MaxSpikeFactor - MinSpikeFactor) * rng.NextDouble();
                var energy = energyAt(utc);

                if (isGap)
                {
                    continue;
                }

                if (isSpike)
                {
                    energy *= spikeFactor;
                }

                var value = decimal.Round((decimal)energy, MeasurementValue.Scale, MidpointRounding.AwayFromZero);
                yield return new MeasurementValue(seriesId, utc, value, MeasurementStatus.Measured);
            }
        }
    }

    private static DateTimeOffset LocalMidnightUtc(DateOnly day)
    {
        // Midnight is never skipped or repeated in Germany (switches happen at 02:00/03:00), so the offset is unambiguous.
        var midnight = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, Berlin.GetUtcOffset(midnight)).ToUniversalTime();
    }

    /// <summary>Published fourth-order dynamization polynomial of the H0 profile: higher load in winter, lower in summer.</summary>
    private static double HouseholdDynamization(int dayOfYear)
    {
        double d = dayOfYear;
        return -3.92e-10 * Math.Pow(d, 4) + 3.2e-7 * Math.Pow(d, 3) - 7.02e-5 * d * d + 2.1e-3 * d + 1.24;
    }

    private static double Noise(Random rng, double amplitude) => 1 + amplitude * (2 * rng.NextDouble() - 1);

    private static void EnsureProbability(double value, string name)
    {
        if (value is < 0 or > 1 || double.IsNaN(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "Probability must be between 0 and 1.");
        }
    }

    private static void EnsurePositive(decimal value, string name)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Value must be positive.");
        }
    }
}
