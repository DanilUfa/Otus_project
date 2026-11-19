namespace VacationPlanner.Importer.Models
{
    internal sealed class ImportError
    {
        public int Id { get; init; }
        public int ImportJobId { get; init; }
        public int RowNumber { get; init; }
        public required string RawData { get; init; }
        public required string Reason { get; init; }

        public DateTime CreatedAt { get; } = DateTime.UtcNow;
    }
}
