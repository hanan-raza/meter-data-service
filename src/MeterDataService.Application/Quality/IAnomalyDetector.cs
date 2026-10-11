using MeterDataService.Domain;

namespace MeterDataService.Application.Quality;

/// <summary>Finds spikes and change points in a load profile and records them as <see cref="SeriesAnnotation"/>s.</summary>
public interface IAnomalyDetector
{
    /// <summary>
    /// Annotates the intervals of the German calendar days <paramref name="firstDay"/> to <paramref name="lastDay"/>,
    /// replacing earlier findings for that period. Values before <paramref name="firstDay"/> serve as history only,
    /// so the first day is judged against what came before it instead of starting cold.
    /// </summary>
    /// <returns>The annotations added for the period.</returns>
    IReadOnlyList<SeriesAnnotation> Annotate(MeasurementSeries series, DateOnly firstDay, DateOnly lastDay);
}
