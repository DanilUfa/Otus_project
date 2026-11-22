namespace VacationPlanner.Importer.Models;

internal sealed class ImportError
{
    public int Id { get; init; }
    public int ImportJobId { get; init; }
    public int RowNumber { get; init; }
    public required string ErrorCode { get; init; }
    public required string Message { get; init; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
}
