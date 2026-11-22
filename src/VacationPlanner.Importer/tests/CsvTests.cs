using System.Text;
using VacationPlanner.Importer.Csv;
using Xunit;

namespace VacationPlanner.Importer.Tests;

public sealed class CsvTests
{
    [Theory]
    [InlineData("")]
    [InlineData(TestCsv.Header)]
    [InlineData("wrong,header\nx")]
    [InlineData(TestCsv.Header + "\n\"unfinished")]
    [InlineData(TestCsv.Header + "\n\"closed\"x")]
    public async Task RejectsInvalidStructure(string contents)
    {
        using var csv = new TestCsv("");
        await File.WriteAllTextAsync(csv.Path, contents);
        await Assert.ThrowsAsync<InvalidDataException>(() => CsvSnapshot.OpenAsync(csv.Path, 2048, 20, default));
    }

    [Fact]
    public async Task StrictUtf8AndLimits()
    {
        using var csv = new TestCsv("a\nb");
        await Assert.ThrowsAsync<InvalidDataException>(() => CsvSnapshot.OpenAsync(csv.Path, 3, 10, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => CsvSnapshot.OpenAsync(csv.Path, 2000, 1, default));
        await File.WriteAllBytesAsync(csv.Path, [0xC0, 0x80]);
        await Assert.ThrowsAsync<DecoderFallbackException>(() => CsvSnapshot.OpenAsync(csv.Path, 2000, 10, default));
    }

    [Fact]
    public async Task ReadsOriginalSnapshotWithBomQuotesAndPhysicalLineNumbers()
    {
        using var csv = new TestCsv("");
        await File.WriteAllTextAsync(csv.Path, TestCsv.Header + "\r\na,b,c,d,e,\"one, \"\"two\"\"\r\nthree\"\nf,g,h,i,j,", new UTF8Encoding(true));
        await using var snapshot = await CsvSnapshot.OpenAsync(csv.Path, 1024, 2, default);
        await File.WriteAllTextAsync(csv.Path, "changed");
        File.Delete(csv.Path);
        var rows = await snapshot.RowsAsync(default).ToListAsync();
        Assert.Equal(2, snapshot.TotalRows);
        Assert.Equal("one, \"two\"\nthree", rows[0].Fields[5]);
        Assert.Equal(4, rows[1].Line);
        Assert.Equal("", rows[1].Fields[5]);
    }

    [Theory]
    [InlineData("2", "0", "2030-01-01", "Type")]
    [InlineData("1", "Annual", "2030-01-01", "UserId")]
    [InlineData("2", "Annual", "2030-02-30", "Dates")]
    [InlineData("2", "Annual", "", "Dates")]
    public void ValidatesContract(string owner, string type, string date, string code)
    {
        var r = new CsvRecord(2, [Guid.NewGuid().ToString(), owner, type, date, "2030-04-05", ""]);
        Assert.Equal(code, RowValidator.Parse(r, 2).Error?.Code);
    }
}
