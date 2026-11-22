using System.Globalization;

namespace VacationPlanner.Importer.Csv;

internal enum LeaveType { Annual, Sick, Unpaid }

internal sealed record LeavePayload(int UserId, LeaveType Type, DateTime StartDate, DateTime EndDate, string Reason);
internal sealed record LeaveRow(Guid RequestId, LeavePayload Payload);
internal sealed record RowIssue(string Code, string Message);

internal static class RowValidator
{
    public static (LeaveRow? Row, RowIssue? Error) Parse(CsvRecord record, int owner)
    {
        var f = record.Fields;
        if (f.Length != 6) return (null, new("Columns", "Expected six columns."));
        if (!Guid.TryParseExact(f[0], "D", out var id) || id == Guid.Empty)
            return (null, new("RequestId", "RequestId must be a nonempty UUID."));
        if (!int.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out var user) || user != owner)
            return (null, new("UserId", "UserId must match the authenticated employee."));
        if (!Enum.TryParse<LeaveType>(f[2], out var type) || Enum.GetName(type) != f[2])
            return (null, new("Type", "Expected Annual, Sick or Unpaid."));
        if (!Date(f[3], out var start) || !Date(f[4], out var end) || start > end)
            return (null, new("Dates", "Invalid date or reversed interval."));
        if (f[5].Length > 500 || f[5].Contains('\0'))
            return (null, new("Reason", "Reason exceeds 500 characters or contains NUL."));
        return (new(id, new(user, type, start, end, f[5])), null);
    }

    private static bool Date(string input, out DateTime date) => DateTime.TryParseExact(input, "yyyy-MM-dd",
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date);
}
