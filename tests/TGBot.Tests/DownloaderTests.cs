using TGBot.Download;
using Xunit;

namespace TGBot.Tests;

/// <summary>
/// <see cref="YtDlpArgumentBuilder"/> 单元测试。
/// </summary>
public class YtDlpArgumentBuilderTests
{
    private static DownloadOptions Options(string? ffmpegDir = null, bool audio = false, bool playlists = false)
        => new(
            "https://example.com/video",
            "/tmp/job",
            "/usr/local/bin/yt-dlp",
            ffmpegDir,
            "mp4",
            audio,
            playlists,
            1_900_000_000,
            TimeSpan.FromMinutes(10));

    [Fact]
    public void Build_ContainsUrlAsLastArg()
    {
        var args = YtDlpArgumentBuilder.Build(Options());
        Assert.Equal("https://example.com/video", args[^1]);
    }

    [Fact]
    public void Build_NoPlaylist_ByDefault()
    {
        var args = YtDlpArgumentBuilder.Build(Options());
        Assert.Contains("--no-playlist", args);
    }

    [Fact]
    public void Build_PlaylistsAllowed_OmitsNoPlaylist()
    {
        var args = YtDlpArgumentBuilder.Build(Options(playlists: true));
        Assert.DoesNotContain("--no-playlist", args);
    }

    [Fact]
    public void Build_ExtractAudio_AddsAudioArgs()
    {
        var args = YtDlpArgumentBuilder.Build(Options(audio: true));
        Assert.Contains("--extract-audio", args);
        Assert.Contains("--audio-format", args);
        Assert.Contains("mp3", args);
    }

    [Fact]
    public void Build_FfmpegDir_AddsLocationArg()
    {
        var args = YtDlpArgumentBuilder.Build(Options("/opt/bin"));
        Assert.Contains("--ffmpeg-location", args);
        Assert.Contains("/opt/bin", args);
    }

    [Fact]
    public void Build_NoFfmpegDir_OmitsLocationArg()
    {
        var args = YtDlpArgumentBuilder.Build(Options());
        Assert.DoesNotContain("--ffmpeg-location", args);
    }

    [Fact]
    public void Build_NeverContainsShellMetacharacters()
    {
        var args = YtDlpArgumentBuilder.Build(Options());
        Assert.All(args, a => Assert.DoesNotContain(';', a));
        Assert.All(args, a => Assert.DoesNotContain('&', a));
        Assert.All(args, a => Assert.DoesNotContain('`', a));
        Assert.All(args, a => Assert.DoesNotContain("$(", a));
        Assert.All(args, a => Assert.DoesNotContain("${", a));
    }

    [Fact]
    public void Build_ContainsMergeFormat()
    {
        var args = YtDlpArgumentBuilder.Build(Options());
        Assert.Contains("--merge-output-format", args);
        Assert.Contains("mp4", args);
    }
}

/// <summary>
/// <see cref="DownloadGate"/> 单元测试。
/// </summary>
public class DownloadGateTests
{
    [Fact]
    public async Task Gate_AllowsUpToMaxConcurrent()
    {
        await using var gate = new DownloadGate(2);
        var first = await gate.AcquireDownloadAsync(CancellationToken.None);
        var second = await gate.AcquireDownloadAsync(CancellationToken.None);

        var thirdStarted = false;
        var third = Task.Run(async () =>
        {
            await gate.AcquireDownloadAsync(CancellationToken.None);
            thirdStarted = true;
        });

        await Task.Delay(100);
        Assert.False(thirdStarted);

        await first.DisposeAsync();
        await second.DisposeAsync();
        await third;
        Assert.True(thirdStarted);
    }

    [Fact]
    public async Task Gate_ExclusiveBlocksNewDownloads()
    {
        await using var gate = new DownloadGate(2);
        var exclusive = await gate.AcquireExclusiveAsync(CancellationToken.None);

        var started = false;
        var dl = Task.Run(async () =>
        {
            await gate.AcquireDownloadAsync(CancellationToken.None);
            started = true;
        });

        await Task.Delay(100);
        Assert.False(started);

        await exclusive.DisposeAsync();
        await dl;
        Assert.True(started);
    }

    [Fact]
    public async Task Gate_ExclusiveWaitsForRunningDownloads()
    {
        await using var gate = new DownloadGate(1);
        var dl = await gate.AcquireDownloadAsync(CancellationToken.None);

        var exclusiveStarted = false;
        var excl = Task.Run(async () =>
        {
            await gate.AcquireExclusiveAsync(CancellationToken.None);
            exclusiveStarted = true;
        });

        await Task.Delay(100);
        Assert.False(exclusiveStarted);

        await dl.DisposeAsync();
        await excl;
        Assert.True(exclusiveStarted);
    }
}

/// <summary>
/// <see cref="JobRegistry"/> 单元测试。
/// </summary>
public class JobRegistryTests
{
    [Fact]
    public void Registry_CountsQueuedAndRunning()
    {
        var reg = new JobRegistry();
        reg.OnEnqueue();
        reg.OnEnqueue();
        reg.OnStart();
        Assert.Equal(1, reg.Queued);
        Assert.Equal(1, reg.Running);
        reg.OnFinish();
        Assert.Equal(0, reg.Running);
    }

    [Fact]
    public void Registry_DuplicateUrl_Rejected()
    {
        var reg = new JobRegistry();
        Assert.True(reg.TryReserveUrl("https://x.io/a"));
        Assert.False(reg.TryReserveUrl("https://x.io/a"));
        reg.ReleaseUrl("https://x.io/a");
        Assert.True(reg.TryReserveUrl("https://x.io/a"));
    }
}

/// <summary>
/// <see cref="YtDlpOutputParser"/> 单元测试。
/// </summary>
public class YtDlpOutputParserTests
{
    [Theory]
    [InlineData("DLP   2.4%| Unknown B/s", 2.4, "Unknown B/s")]
    [InlineData("DLP  73.6%|  28.60MiB/s", 73.6, "28.60MiB/s")]
    [InlineData("DLP 100.0%|21.38MiB/s", 100.0, "21.38MiB/s")]
    public void ParseProgress_RealFormats(string line, double percent, string speed)
    {
        var p = YtDlpOutputParser.ParseProgress(line);
        Assert.NotNull(p);
        Assert.Equal(percent, p.Percent!.Value, 2);
        Assert.Equal(speed, p.SpeedText);
    }

    [Theory]
    [InlineData("[download] Destination: media.mp4")]
    [InlineData("[info] test_src: Downloading 1 format(s): mp4")]
    [InlineData("")]
    [InlineData("DLP --.-%| Unknown B/s")]
    [InlineData("DLP  N/A")]
    public void ParseProgress_NonProgressLines_Null(string line)
    {
        Assert.Null(YtDlpOutputParser.ParseProgress(line));
    }

    [Fact]
    public void ParseMeta_ValidLine_ReturnsTitleAndDuration()
    {
        var result = YtDlpOutputParser.ParseMeta("META\u001fabc123\u001f我的标题\u001fmp4\u001f125");
        Assert.NotNull(result);
        Assert.Equal("我的标题", result!.Title);
        Assert.Equal(125, result.DurationSeconds);
    }

    [Fact]
    public void ParseMeta_NaDuration_NullDuration()
    {
        var result = YtDlpOutputParser.ParseMeta("META\u001fx\u001fTitle\u001fmp4\u001fNA");
        Assert.NotNull(result);
        Assert.Null(result!.DurationSeconds);
    }

    [Fact]
    public void ParseMeta_WithSize_ParsesSize()
    {
        var result = YtDlpOutputParser.ParseMeta("META\u001fx\u001fTitle\u001fmp4\u001fNA\u001f1048576\u001f2097152");
        Assert.NotNull(result);
        Assert.Equal(2_097_152, result!.SizeBytes);
    }

    [Fact]
    public void ParseMeta_NonMetaLine_Null()
    {
        Assert.Null(YtDlpOutputParser.ParseMeta("FILE\u001f/path"));
    }

    [Theory]
    [InlineData("[download] File is larger than max-filesize (5510872 bytes > 1024 bytes). Aborting.")]
    [InlineData("ERROR: Requested file is larger than 1024 bytes, aborting.")]
    public void IsTooLargeMessage_DetectsSizeAbort(string line)
    {
        Assert.True(YtDlpOutputParser.IsTooLargeMessage(line));
    }

    [Fact]
    public void IsTooLargeMessage_NormalLine_False()
    {
        Assert.False(YtDlpOutputParser.IsTooLargeMessage("[download] Destination: media.mp4"));
    }
}
