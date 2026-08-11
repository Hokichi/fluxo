using Fluxo.Installer.Services;
using Xunit;

namespace Fluxo.Tests.Installer;

public sealed class DotNetRuntimeReleaseResolverTests
{
    [Fact]
    public void DotNetRuntimeReleaseResolver_ResolveLatestWindowsDesktopRuntimeInstaller_SelectsLatest10WinX64Exe()
    {
        const string indexJson = """
        {
          "releases-index": [
            {
              "channel-version": "10.0",
              "latest-release": "10.0.8",
              "releases.json": "https://example.test/10/releases.json"
            }
          ]
        }
        """;
        const string releasesJson = """
        {
          "releases": [
            {
              "release-version": "10.0.7",
              "windowsdesktop": {
                "files": [
                  {
                    "name": "windowsdesktop-runtime-win-x64.exe",
                    "rid": "win-x64",
                    "url": "https://example.test/10.0.7/windowsdesktop-runtime-win-x64.exe",
                    "hash": "older"
                  }
                ]
              }
            },
            {
              "release-version": "10.0.8",
              "windowsdesktop": {
                "files": [
                  {
                    "name": "windowsdesktop-runtime-win-x64.exe",
                    "rid": "win-x64",
                    "url": "https://example.test/10.0.8/windowsdesktop-runtime-win-x64.exe",
                    "hash": "abc123"
                  },
                  {
                    "name": "windowsdesktop-runtime-win-x86.exe",
                    "rid": "win-x86",
                    "url": "https://example.test/10.0.8/windowsdesktop-runtime-win-x86.exe",
                    "hash": "def456"
                  },
                  {
                    "name": "windowsdesktop-runtime-win-x64.zip",
                    "rid": "win-x64",
                    "url": "https://example.test/10.0.8/windowsdesktop-runtime-win-x64.zip",
                    "hash": "zip789"
                  }
                ]
              }
            }
          ]
        }
        """;

        var result = DotNetRuntimeReleaseResolver.ResolveLatestWindowsDesktopRuntimeInstaller(
            indexJson,
            releasesJson,
            channelVersion: "10.0",
            rid: "win-x64");

        Assert.Equal("10.0.8", result.Version);
        Assert.Equal("win-x64", result.Rid);
        Assert.Equal("windowsdesktop-runtime-win-x64.exe", result.FileName);
        Assert.Equal("https://example.test/10.0.8/windowsdesktop-runtime-win-x64.exe", result.Url);
        Assert.Equal("abc123", result.Hash);
        Assert.Equal("https://example.test/10/releases.json", result.ReleasesJsonUrl);
    }

    [Fact]
    public void DotNetRuntimeReleaseResolver_ResolveLatestWindowsDesktopRuntimeInstaller_SkipsIncompleteNonTargetFiles()
    {
        const string indexJson = """
        {
          "releases-index": [
            {
              "channel-version": "10.0",
              "latest-release": "10.0.8",
              "releases.json": "https://example.test/10/releases.json"
            }
          ]
        }
        """;
        const string releasesJson = """
        {
          "releases": [
            {
              "release-version": "10.0.8",
              "windowsdesktop": {
                "files": [
                  {
                    "name": "windowsdesktop-runtime-10.0.8-win-x86.exe",
                    "rid": "win-x86"
                  },
                  {
                    "name": "windowsdesktop-runtime-10.0.8-win-x64.zip",
                    "rid": "win-x64"
                  },
                  {
                    "name": "windowsdesktop-runtime-10.0.8-win-x64.exe",
                    "rid": "win-x64",
                    "url": "https://example.test/windowsdesktop-runtime-10.0.8-win-x64.exe",
                    "hash": "abc123"
                  }
                ]
              }
            }
          ]
        }
        """;

        var result = DotNetRuntimeReleaseResolver.ResolveLatestWindowsDesktopRuntimeInstaller(
            indexJson,
            releasesJson,
            channelVersion: "10.0",
            rid: "win-x64");

        Assert.Equal("windowsdesktop-runtime-10.0.8-win-x64.exe", result.FileName);
        Assert.Equal("https://example.test/windowsdesktop-runtime-10.0.8-win-x64.exe", result.Url);
        Assert.Equal("abc123", result.Hash);
    }

    [Fact]
    public void DotNetRuntimeReleaseResolver_ResolveReleasesJsonUrl_ReturnsUrlForChannel()
    {
        const string indexJson = """
        {
          "releases-index": [
            {
              "channel-version": "9.0",
              "releases.json": "https://example.test/9/releases.json"
            },
            {
              "channel-version": "10.0",
              "releases.json": "https://example.test/10/releases.json"
            }
          ]
        }
        """;

        var result = DotNetRuntimeReleaseResolver.ResolveReleasesJsonUrl(indexJson, channelVersion: "10.0");

        Assert.Equal("https://example.test/10/releases.json", result);
    }

    [Fact]
    public void DotNetRuntimeReleaseResolver_ResolveReleasesJsonUrl_Throws_WhenChannelMissing()
    {
        const string indexJson = """{ "releases-index": [] }""";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DotNetRuntimeReleaseResolver.ResolveReleasesJsonUrl(indexJson, channelVersion: "10.0"));

        Assert.Contains("release channel 10.0", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DotNetRuntimeReleaseResolver_ResolveLatestWindowsDesktopRuntimeInstaller_Throws_WhenChannelMissing()
    {
        const string indexJson = """{ "releases-index": [] }""";
        const string releasesJson = """{ "releases": [] }""";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DotNetRuntimeReleaseResolver.ResolveLatestWindowsDesktopRuntimeInstaller(
                indexJson,
                releasesJson,
                channelVersion: "10.0",
                rid: "win-x64"));

        Assert.Contains("10.0", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DotNetRuntimeReleaseResolver_ResolveReleasesJsonUrl_ThrowsDomainError_WhenReleaseIndexJsonMalformed()
    {
        const string indexJson = """{ "releases-index": [ """;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DotNetRuntimeReleaseResolver.ResolveReleasesJsonUrl(indexJson, channelVersion: "10.0"));

        Assert.Contains("release index JSON", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DotNetRuntimeReleaseResolver_ResolveLatestWindowsDesktopRuntimeInstaller_ThrowsDomainError_WhenReleasesJsonMalformed()
    {
        const string indexJson = """
        {
          "releases-index": [
            {
              "channel-version": "10.0",
              "latest-release": "10.0.8",
              "releases.json": "https://example.test/10/releases.json"
            }
          ]
        }
        """;
        const string releasesJson = """{ "releases": [ """;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DotNetRuntimeReleaseResolver.ResolveLatestWindowsDesktopRuntimeInstaller(
                indexJson,
                releasesJson,
                channelVersion: "10.0",
                rid: "win-x64"));

        Assert.Contains("releases JSON", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DotNetRuntimeReleaseResolver_ResolveReleasesJsonUrl_ThrowsDomainError_WhenReleasesIndexIsNotArray()
    {
        const string indexJson = """{ "releases-index": {} }""";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DotNetRuntimeReleaseResolver.ResolveReleasesJsonUrl(indexJson, channelVersion: "10.0"));

        Assert.Contains("releases-index", ex.Message, StringComparison.Ordinal);
        Assert.Contains("array", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DotNetRuntimeReleaseResolver_ResolveLatestWindowsDesktopRuntimeInstaller_ThrowsDomainError_WhenWindowsDesktopFilesIsNotArray()
    {
        const string indexJson = """
        {
          "releases-index": [
            {
              "channel-version": "10.0",
              "latest-release": "10.0.8",
              "releases.json": "https://example.test/10/releases.json"
            }
          ]
        }
        """;
        const string releasesJson = """
        {
          "releases": [
            {
              "release-version": "10.0.8",
              "windowsdesktop": { "files": {} }
            }
          ]
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DotNetRuntimeReleaseResolver.ResolveLatestWindowsDesktopRuntimeInstaller(
                indexJson,
                releasesJson,
                channelVersion: "10.0",
                rid: "win-x64"));

        Assert.Contains("windowsdesktop files", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("array", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DotNetRuntimeReleaseResolver_ResolveLatestWindowsDesktopRuntimeInstaller_Throws_WhenWinX64DesktopInstallerMissing()
    {
        const string indexJson = """
        {
          "releases-index": [
            {
              "channel-version": "10.0",
              "latest-release": "10.0.8",
              "releases.json": "https://example.test/10/releases.json"
            }
          ]
        }
        """;
        const string releasesJson = """
        {
          "releases": [
            {
              "release-version": "10.0.8",
              "windowsdesktop": { "files": [] }
            }
          ]
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DotNetRuntimeReleaseResolver.ResolveLatestWindowsDesktopRuntimeInstaller(
                indexJson,
                releasesJson,
                channelVersion: "10.0",
                rid: "win-x64"));

        Assert.Contains("Windows Desktop Runtime", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DotNetRuntimeReleaseResolver_ResolveLatestWindowsDesktopRuntimeInstaller_Throws_WhenInstallerHashMissing()
    {
        const string indexJson = """
        {
          "releases-index": [
            {
              "channel-version": "10.0",
              "latest-release": "10.0.8",
              "releases.json": "https://example.test/10/releases.json"
            }
          ]
        }
        """;
        const string releasesJson = """
        {
          "releases": [
            {
              "release-version": "10.0.8",
              "windowsdesktop": {
                "files": [
                  {
                    "name": "windowsdesktop-runtime-10.0.8-win-x64.exe",
                    "rid": "win-x64",
                    "url": "https://example.test/windowsdesktop-runtime-10.0.8-win-x64.exe"
                  }
                ]
              }
            }
          ]
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DotNetRuntimeReleaseResolver.ResolveLatestWindowsDesktopRuntimeInstaller(
                indexJson,
                releasesJson,
                channelVersion: "10.0",
                rid: "win-x64"));

        Assert.Contains("hash", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
