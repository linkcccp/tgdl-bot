// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Net;
using TGBot.Update;
using Xunit;

namespace TGBot.Tests;

/// <summary>
/// 更新器远程集成测试（依赖真实网络，不可达时静默跳过）。
/// </summary>
public class UpdaterIntegrationTests
{
    [Fact]
    public async Task UpdateYtDlp_DownloadsAndParsesVersion()
    {
        if (!await IsReachableAsync("https://github.com"))
        {
            return;
        }

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("tgdl-bot/1.0");
        var runner = new SystemProcessRunner();

        var source = new YtDlpToolSource(http);
        var latest = await source.GetLatestVersionAsync(CancellationToken.None);
        Assert.NotNull(latest);

        var tmp = Path.Combine(Path.GetTempPath(), "tgdl-upd-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            var binary = await source.DownloadBinaryAsync(tmp, CancellationToken.None);
            var output = await runner.RunAsync(binary, new[] { "--version" }, null, TimeSpan.FromSeconds(30), CancellationToken.None);
            Assert.Equal(0, output.ExitCode);
            Assert.NotNull(BinaryVersionParser.ParseYtDlp(output.StdOut));
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    private static async Task<bool> IsReachableAsync(string url)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), HttpCompletionOption.ResponseHeadersRead);
            return response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Found or HttpStatusCode.Redirect;
        }
        catch
        {
            return false;
        }
    }
}
