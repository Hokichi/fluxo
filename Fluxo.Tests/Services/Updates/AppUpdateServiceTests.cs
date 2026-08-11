using System.Net;
using System.Text;
using Fluxo.Services.Updates;
using Xunit;

namespace Fluxo.Tests.Services.Updates;

public sealed class AppUpdateServiceTests
{
    [Fact]
    public async Task AppUpdateService_CheckForUpdatesAsync_WhenLatestReleaseIsNewer_ReturnsInstallerAsset()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "tag_name": "v1.2.0",
                  "assets": [
                    {
                      "name": "fluxo-1.2.0-Installer.exe",
                      "browser_download_url": "https://example.test/fluxo-1.2.0-Installer.exe"
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json")
        });

        var sut = new AppUpdateService(new HttpClient(handler));

        var result = await sut.CheckForUpdatesAsync("1.0.0");

        Assert.Equal(AppUpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("1.2.0", result.LatestVersion);
        Assert.Equal("fluxo-1.2.0-Installer.exe", result.InstallerAssetName);
        Assert.Equal("https://example.test/fluxo-1.2.0-Installer.exe", result.InstallerDownloadUrl);
    }

    [Fact]
    public async Task AppUpdateService_CheckForUpdatesAsync_WhenLatestReleaseIsSameVersion_ReturnsUpToDate()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "tag_name": "v1.0.0",
                  "assets": [
                    {
                      "name": "fluxo-1.0.0-Installer.exe",
                      "browser_download_url": "https://example.test/fluxo-1.0.0-Installer.exe"
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json")
        });

        var sut = new AppUpdateService(new HttpClient(handler));

        var result = await sut.CheckForUpdatesAsync("1.0.0");

        Assert.Equal(AppUpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task AppUpdateService_CheckForUpdatesAsync_WhenNetworkFails_ReturnsConnectionError()
    {
        var sut = new AppUpdateService(new HttpClient(
            new StubHttpMessageHandler(_ => throw new HttpRequestException("network unavailable"))));

        var result = await sut.CheckForUpdatesAsync("1.0.0");

        Assert.Equal(AppUpdateCheckStatus.Error, result.Status);
        Assert.Contains("internet", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AppUpdateService_DownloadInstallerAsync_WritesInstallerToTempFile()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"fluxo-update-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var handler = new StubHttpMessageHandler(request =>
            {
                Assert.Equal("https://example.test/fluxo-1.2.0-Installer.exe", request.RequestUri?.ToString());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1, 2, 3, 4])
                };
            });

            var sut = new AppUpdateService(new HttpClient(handler), () => tempDirectory);

            var path = await sut.DownloadInstallerAsync(
                "https://example.test/fluxo-1.2.0-Installer.exe",
                "fluxo-1.2.0-Installer.exe");

            Assert.Equal(Path.Combine(tempDirectory, "fluxo-1.2.0-Installer.exe"), path);
            Assert.Equal([1, 2, 3, 4], await File.ReadAllBytesAsync(path));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task AppUpdateService_DownloadInstallerAsync_WhenContentLengthKnown_ReportsProgressPercentage()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"fluxo-update-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4])
            });
            var progressValues = new List<double>();
            var progress = new InlineProgress<double>(progressValues.Add);

            var sut = new AppUpdateService(new HttpClient(handler), () => tempDirectory);

            await sut.DownloadInstallerAsync(
                "https://example.test/fluxo-1.2.0-Installer.exe",
                "fluxo-1.2.0-Installer.exe",
                progress);

            Assert.Contains(100d, progressValues);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task AppUpdateService_DownloadInstallerAsync_WhenStreamReportsTinyChunks_ThrottlesProgressReports()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"fluxo-update-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new OneByteAtATimeContent(1_000)
            });
            var progressValues = new List<double>();
            var progress = new InlineProgress<double>(progressValues.Add);

            var sut = new AppUpdateService(new HttpClient(handler), () => tempDirectory);

            await sut.DownloadInstallerAsync(
                "https://example.test/fluxo-1.2.0-Installer.exe",
                "fluxo-1.2.0-Installer.exe",
                progress);

            Assert.Equal(0d, progressValues[0]);
            Assert.Equal(100d, progressValues[^1]);
            Assert.True(
                progressValues.Count <= 102,
                $"Expected throttled progress reports, but received {progressValues.Count} updates.");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task AppUpdateService_DownloadInstallerAsync_RequestsLargeReadBuffer()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"fluxo-update-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var content = new ReadBufferInspectingContent();
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            });
            var sut = new AppUpdateService(new HttpClient(handler), () => tempDirectory);

            await sut.DownloadInstallerAsync(
                "https://example.test/fluxo-1.2.0-Installer.exe",
                "fluxo-1.2.0-Installer.exe");

            Assert.True(
                content.LargestRequestedReadBuffer >= 10_485_760,
                $"Expected a read buffer of at least 10 MiB, but saw {content.LargestRequestedReadBuffer} bytes.");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task AppUpdateService_DownloadInstallerAsync_WhenStreamCancels_DeletesPartialInstaller()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"fluxo-update-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var destinationPath = Path.Combine(tempDirectory, "fluxo-1.2.0-Installer.exe");
        try
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new CancelAfterFirstReadContent()
            });
            var sut = new AppUpdateService(new HttpClient(handler), () => tempDirectory);

            await Assert.ThrowsAsync<OperationCanceledException>(() => sut.DownloadInstallerAsync(
                "https://example.test/fluxo-1.2.0-Installer.exe",
                "fluxo-1.2.0-Installer.exe",
                CancellationToken.None));

            Assert.False(File.Exists(destinationPath));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value)
        {
            report(value);
        }
    }

    private sealed class CancelAfterFirstReadContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new NotSupportedException();

        protected override bool TryComputeLength(out long length)
        {
            length = 4;
            return true;
        }

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new CancelAfterFirstReadStream());
    }

    private sealed class CancelAfterFirstReadStream : Stream
    {
        private bool _hasRead;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 4;
        public override long Position { get; set; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_hasRead)
                throw new OperationCanceledException();

            _hasRead = true;
            buffer[offset] = 1;
            return 1;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_hasRead)
                throw new OperationCanceledException(cancellationToken);

            _hasRead = true;
            buffer.Span[0] = 1;
            return ValueTask.FromResult(1);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class OneByteAtATimeContent(int length) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new NotSupportedException();

        protected override bool TryComputeLength(out long contentLength)
        {
            contentLength = length;
            return true;
        }

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new OneByteAtATimeStream(length));
    }

    private sealed class OneByteAtATimeStream(int length) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= length)
                return 0;

            buffer[offset] = 1;
            _position++;
            return 1;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= length)
                return ValueTask.FromResult(0);

            buffer.Span[0] = 1;
            _position++;
            return ValueTask.FromResult(1);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ReadBufferInspectingContent : HttpContent
    {
        public int LargestRequestedReadBuffer { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new NotSupportedException();

        protected override bool TryComputeLength(out long length)
        {
            length = 1;
            return true;
        }

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new ReadBufferInspectingStream(value => LargestRequestedReadBuffer = value));
    }

    private sealed class ReadBufferInspectingStream(Action<int> recordBufferLength) : Stream
    {
        private bool _hasRead;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 1;
        public override long Position { get; set; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            recordBufferLength(count);
            if (_hasRead)
                return 0;

            _hasRead = true;
            buffer[offset] = 1;
            return 1;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            recordBufferLength(buffer.Length);
            if (_hasRead)
                return ValueTask.FromResult(0);

            _hasRead = true;
            buffer.Span[0] = 1;
            return ValueTask.FromResult(1);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
