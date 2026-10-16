using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornal.Desktop.Updates;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;
using Xunit;

namespace Jornal.Tests;

public sealed class GithubUpdatesTests
{
    [Fact]
    public async Task StableUsesLatestEndpointAndCalVerIsComparedNumerically()
    {
        using var fixture = new FeedFixture();
        var updater = fixture.Updater();
        Assert.True(updater.IsInstalled);
        var update = await updater.CheckAsync(false, TestContext.Current.CancellationToken);
        Assert.Equal("2026.10.0", update!.Version);
        Assert.EndsWith("/releases/latest", fixture.Downloader.LastApiUrl);
        Assert.Equal(0, fixture.Downloader.FileDownloads);
        await updater.DownloadAsync(update, new Progress<int>(), TestContext.Current.CancellationToken);
        Assert.Equal(fixture.Downloader.Package, File.ReadAllBytes(Path.Combine(fixture.Directory, "JournalTest-2026.10.0-full.nupkg")));
        Assert.False(fixture.Downloader.UsedAuthorization);
    }

    [Fact]
    public async Task PreviewSourceIncludesTestReleaseAndStableNeverAcceptsIt()
    {
        using var fixture = new FeedFixture(); fixture.Downloader.Preview = true;
        var updater = fixture.Updater();
        var update = await updater.CheckAsync(true, TestContext.Current.CancellationToken);
        Assert.Equal("2026.11.0-beta.1", update!.Version);
        Assert.Contains("/releases?", fixture.Downloader.LastApiUrl);
        // Даже ошибочно помеченный стабильным GitHub Release не разрешает beta в стабильном канале.
        fixture.Downloader.MislabelPreview = true;
        await Assert.ThrowsAsync<InvalidDataException>(() => updater.CheckAsync(false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NoPublishedStableReleaseAndOlderVersionsAreNotUpdates()
    {
        using var fixture = new FeedFixture(); fixture.Downloader.NoStable = true;
        Assert.Null(await fixture.Updater().CheckAsync(false, TestContext.Current.CancellationToken));
        fixture.Downloader.NoStable = false;
        Assert.Null(await fixture.Updater("2026.20.0").CheckAsync(false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PackageForAnotherAppIsRejectedBeforeItCanBeDownloaded()
    {
        using var fixture = new FeedFixture(); fixture.Downloader.PackageId = "AnotherApp";
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Updater().CheckAsync(false, TestContext.Current.CancellationToken));
        Assert.Equal(0, fixture.Downloader.FileDownloads);
    }

    [Fact]
    public async Task CorruptedPackageAndUnrequestedRestartAreRejected()
    {
        using var fixture = new FeedFixture();
        var updater = fixture.Updater();
        var update = await updater.CheckAsync(false, TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(() => updater.ScheduleRestart(update!));
        fixture.Downloader.CorruptPackage = true;
        await Assert.ThrowsAsync<Velopack.Exceptions.ChecksumFailedException>(() => updater.DownloadAsync(update!, new Progress<int>(), TestContext.Current.CancellationToken));
        Assert.Throws<InvalidOperationException>(() => updater.ScheduleRestart(update!));
        Assert.False(File.Exists(Path.Combine(fixture.Directory, "JournalTest-2026.10.0-full.nupkg")));
    }

    private sealed class FeedFixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "journal-feed-" + Guid.NewGuid().ToString("N"));
        public FakeGithubDownloader Downloader { get; } = new();
        public FeedFixture() => System.IO.Directory.CreateDirectory(Directory);
        public VelopackAppUpdater Updater(string installed = "2026.9.1") => new(preview => new UpdateManager(
            new JournalGithubSource(preview, Downloader), new UpdateOptions { ExplicitChannel = "test", AllowVersionDowngrade = false },
            new TestVelopackLocator("JournalTest", installed, Directory)));
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }

    private sealed class FakeGithubDownloader : IFileDownloader
    {
        public byte[] Package { get; } = Encoding.UTF8.GetBytes("ARTIFICIAL PACKAGE CONTENT");
        public string PackageId { get; set; } = "JournalTest";
        public bool Preview { get; set; }
        public bool MislabelPreview { get; set; }
        public bool NoStable { get; set; }
        public bool CorruptPackage { get; set; }
        public int FileDownloads { get; private set; }
        public bool UsedAuthorization { get; private set; }
        public string LastApiUrl { get; private set; } = "";
        public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            LastApiUrl = url; UsedAuthorization |= headers?.ContainsKey("Authorization") == true;
            if (NoStable) throw new HttpRequestException("No stable release", null, HttpStatusCode.NotFound);
            var release = new { name = "Journal", prerelease = Preview && !MislabelPreview, published_at = "2026-10-16T10:00:00Z",
                assets = new[] { new { name = "releases.test.json", browser_download_url = "https://fixture.test/feed.json" },
                    new { name = FileName, browser_download_url = "https://fixture.test/package.nupkg" } } };
            return Task.FromResult(url.EndsWith("/latest", StringComparison.Ordinal) ? JsonSerializer.Serialize(release) : JsonSerializer.Serialize(new[] { release }));
        }
        private string Version => Preview ? "2026.11.0-beta.1" : "2026.10.0";
        private string FileName => $"JournalTest-{Version}-full.nupkg";
        public Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            UsedAuthorization |= headers?.ContainsKey("Authorization") == true;
            var feed = new { Assets = new[] { new { PackageId, Version, Type = "Full", FileName,
                SHA256 = Convert.ToHexString(SHA256.HashData(Package)), Size = Package.Length, NotesMarkdown = "Изменения для проверки" } } };
            return Task.FromResult(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(feed)));
        }
        public Task DownloadFile(string url, string targetFile, Action<int> progress, IDictionary<string, string>? headers = null, double timeout = 30, CancellationToken cancelToken = default)
        {
            FileDownloads++; UsedAuthorization |= headers?.ContainsKey("Authorization") == true;
            File.WriteAllBytes(targetFile, CorruptPackage ? Encoding.UTF8.GetBytes("CORRUPT") : Package); progress(100);
            return Task.CompletedTask;
        }
    }
}
