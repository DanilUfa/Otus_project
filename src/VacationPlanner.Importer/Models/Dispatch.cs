namespace VacationPlanner.Importer.Models;

// A committed reservation survives a lost response or a process crash.
internal sealed class Dispatch
{
    public Guid RequestId { get; init; }
    public int ImportJobId { get; init; }
    public int RowNumber { get; init; }
    public int? LeaveId { get; set; }
    public bool Rejected { get; set; }
}
