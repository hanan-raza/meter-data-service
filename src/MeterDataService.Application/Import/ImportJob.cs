namespace MeterDataService.Application.Import;

/// <summary>A delivered CSV file waiting to be imported (Importauftrag).</summary>
/// <param name="Id">Assigned on receipt; the client polls the job status with it.</param>
/// <param name="MarketLocationId">Market location (Marktlokation, MaLo) the values belong to.</param>
/// <param name="FileName">Original file name, kept for traceability in logs and status.</param>
/// <param name="Content">The whole CSV text, read on receipt so the upload stream can be released immediately.</param>
/// <param name="ReceivedAt">When the file was accepted, in UTC.</param>
public sealed record ImportJob(Guid Id, string MarketLocationId, string FileName, string Content, DateTimeOffset ReceivedAt);
