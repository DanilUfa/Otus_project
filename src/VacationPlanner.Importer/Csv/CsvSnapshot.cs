using System.Runtime.CompilerServices;
using System.Text;

namespace VacationPlanner.Importer.Csv;

internal sealed record CsvRecord(int Line, string[] Fields);

internal sealed class CsvSnapshot : IAsyncDisposable
{
    private static readonly string[] Header = ["RequestId", "UserId", "Type", "StartDate", "EndDate", "Reason"];
    private readonly FileStream snapshot;
    public int TotalRows { get; private set; }

    private CsvSnapshot(FileStream snapshot) => this.snapshot = snapshot;

    public static async Task<CsvSnapshot> OpenAsync(string path, long maxBytes, int maxRows, CancellationToken ct)
    {
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (source.Length > maxBytes) throw new InvalidDataException("CSV exceeds MaxFileBytes.");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var file = new FileStream(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), options);
        var result = new CsvSnapshot(file);
        try
        {
            var buffer = new byte[65536];
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                if (file.Position + read > maxBytes) throw new InvalidDataException("CSV exceeds MaxFileBytes.");
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            await foreach (var row in result.RowsAsync(ct))
                if (++result.TotalRows > maxRows) throw new InvalidDataException("CSV exceeds MaxRows.");
            if (result.TotalRows == 0) throw new InvalidDataException("CSV contains no data rows.");
            return result;
        }
        catch
        {
            await result.DisposeAsync();
            throw;
        }
    }

    public async IAsyncEnumerable<CsvRecord> RowsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        snapshot.Position = 0;
        using var reader = new StreamReader(snapshot, new UTF8Encoding(false, true), false, 16384, true);
        bool header = true, quoted = false, closed = false;
        int lineNumber = 0, firstLine = 1;
        var fields = new List<string>();
        var field = new StringBuilder();
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            ct.ThrowIfCancellationRequested();
            lineNumber++;
            if (lineNumber == 1 && line.StartsWith('\uFEFF')) line = line[1..];
            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (quoted)
                {
                    if (ch != '"') field.Append(ch);
                    else if (i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else if (ch == ',')
                {
                    fields.Add(field.ToString());
                    if (fields.Count > 64) throw new InvalidDataException($"Too many columns at line {firstLine}.");
                    field.Clear();
                    closed = false;
                }
                else if (ch == '"' && field.Length == 0 && !closed) quoted = true;
                else if (ch == '"' || closed) throw new InvalidDataException($"Invalid quoting at line {lineNumber}.");
                else field.Append(ch);
            }
            if (quoted) { field.Append('\n'); continue; }
            fields.Add(field.ToString());
            if (header)
            {
                if (!fields.SequenceEqual(Header)) throw new InvalidDataException("Invalid CSV header.");
                header = false;
            }
            else yield return new CsvRecord(firstLine, fields.ToArray());
            fields.Clear();
            field.Clear();
            closed = false;
            firstLine = lineNumber + 1;
        }
        if (quoted) throw new InvalidDataException($"Unterminated quoted field at line {firstLine}.");
    }

    public ValueTask DisposeAsync() => snapshot.DisposeAsync();
}
