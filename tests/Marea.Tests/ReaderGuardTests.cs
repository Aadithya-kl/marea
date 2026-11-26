using Marea.Core;

namespace Marea.Tests;

/// <summary>Mass-read detection and blocking of programs that bring evaporated files back.</summary>
public sealed class ReaderGuardTests
{
    private const string Backup = @"\Device\HarddiskVolume3\Program Files\Backup\backup.exe";
    private DateTime _now = new(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc);

    private ReaderGuard Guard(MareaSettings? s = null) => new(s ?? new MareaSettings(), () => _now);

    [Fact]
    public void ManyFilesFromOneProgramRaiseOneAlert()
    {
        var g = Guard();
        MassRead? alert = null;
        for (var i = 0; i < ReaderGuard.FileThreshold; i++)
        {
            alert ??= g.Record(Backup, $@"C:\m\f{i}.safetensors", 1 << 20);
        }
        Assert.NotNull(alert);
        Assert.Equal("backup.exe", alert!.Name);
        Assert.Equal(ReaderGuard.FileThreshold, alert.Files);
        Assert.Null(g.Record(Backup, @"C:\m\extra.safetensors", 1)); // once per window
    }

    [Fact]
    public void ChunksOfTheSameFileCountOnce()
    {
        var g = Guard();
        for (var i = 0; i < 50; i++)
        {
            Assert.Null(g.Record(Backup, @"C:\m\big.gguf", 1 << 20));
        }
    }

    [Fact]
    public void ReadsOlderThanTheWindowAreForgotten()
    {
        var g = Guard();
        for (var i = 0; i < ReaderGuard.FileThreshold - 1; i++)
        {
            g.Record(Backup, $@"C:\m\f{i}", 1);
        }
        _now += ReaderGuard.Window + TimeSpan.FromMinutes(1);
        Assert.Null(g.Record(Backup, @"C:\m\late", 1));
    }

    [Fact]
    public void OneHugeFileIsEnough()
    {
        Assert.NotNull(Guard().Record(Backup, @"C:\m\huge.gguf", ReaderGuard.ByteThreshold));
    }

    [Fact]
    public void MareaItselfAndAllowedProgramsNeverAlert()
    {
        var s = new MareaSettings { AllowedReaders = [Backup] };
        var g = Guard(s);
        for (var i = 0; i < 20; i++)
        {
            Assert.Null(g.Record(Backup, $@"C:\m\a{i}", 1));
            Assert.Null(g.Record(@"\Device\HarddiskVolume3\Tools\Marea\Marea.exe", $@"C:\m\b{i}", 1));
            Assert.Null(g.Record(null, $@"C:\m\c{i}", 1));
        }
    }

    [Fact]
    public void BlockedProgramsAreRecognisedCaseInsensitively()
    {
        var g = Guard(new MareaSettings { BlockedReaders = [Backup] });
        Assert.True(g.IsBlocked(Backup.ToUpperInvariant()));
        Assert.False(g.IsBlocked(@"\Device\HarddiskVolume3\Python\python.exe"));
        Assert.False(g.IsBlocked(null));
    }

    [Fact]
    public void UnusedModelsUseTheLatestFileActivity()
    {
        var now = new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc);
        FileRecord F(int daysAgo) => new()
        {
            Path = $@"C:\m\f{daysAgo}", Root = @"C:\m", Size = 1, Sha256 = daysAgo.ToString(),
            LastAccessUtc = now.AddDays(-daysAgo), MTimeTicks = now.AddDays(-400).Ticks,
        };
        var model = new ModelEntry { Key = "k", Name = "m", Kind = ModelKind.Folder, Folder = @"C:\m", Files = [F(200), F(30)], Meta = [null, null] };
        Assert.False(model.UnusedFor(90, now)); // one file was used 30 days ago
        Assert.True(model.UnusedFor(20, now));
    }
}
