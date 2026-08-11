using System.Globalization;
using Fluxo.Services.Logging;
using Xunit;

namespace Fluxo.Tests.Services.Logging;

[CollectionDefinition("Fluxo logging", DisableParallelization = true)]
public sealed class FluxoLoggingCollection;

[Collection("Fluxo logging")]
public sealed class FluxoLogServiceTests : IDisposable
{
    private readonly string _originalLocalAppData;
    private readonly string _tempLocalAppData;

    public FluxoLogServiceTests()
    {
        _originalLocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _tempLocalAppData = Path.Combine(Path.GetTempPath(), "fluxo-log-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempLocalAppData);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", _tempLocalAppData);
    }

    public void Dispose()
    {
        FluxoLogManager.CloseAndFlush();
        Environment.SetEnvironmentVariable("LOCALAPPDATA", _originalLocalAppData);

        try
        {
            if (Directory.Exists(_tempLocalAppData))
                Directory.Delete(_tempLocalAppData, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; test assertions already completed.
        }
    }

    [Fact]
    public void FluxoLogService_Initialize_CreatesSplitLogFolders()
    {
        FluxoLogManager.Initialize("User");

        Assert.True(Directory.Exists(Path.Combine(_tempLocalAppData, "fluxo", "logs", "db")));
        Assert.True(Directory.Exists(Path.Combine(_tempLocalAppData, "fluxo", "logs", "issues")));
        Assert.True(Directory.Exists(Path.Combine(_tempLocalAppData, "fluxo", "logs", "others")));
    }

    [Fact]
    public void FluxoLogService_LogInformation_RoutesEfCoreMessagesToDb_AndNormalMessagesToOthers()
    {
        FluxoLogManager.Initialize("User");

        FluxoLogManager.LogInformation("EF Core: SELECT 1");
        FluxoLogManager.LogInformation("Boot stage started.");
        FluxoLogManager.CloseAndFlush();

        var dbLog = ReadSingleLog(Path.Combine(_tempLocalAppData, "fluxo", "logs", "db"));
        var otherLog = ReadSingleLog(Path.Combine(_tempLocalAppData, "fluxo", "logs", "others"));

        Assert.Contains("EF Core: SELECT 1", dbLog);
        Assert.DoesNotContain("Boot stage started.", dbLog);
        Assert.Contains("Boot stage started.", otherLog);
        Assert.DoesNotContain("EF Core: SELECT 1", otherLog);
    }

    [Fact]
    public void FluxoLogService_LogError_WritesIssueLogAndFullExceptionFileWithRequestedName()
    {
        FluxoLogManager.Initialize("User");
        var exception = CreateNestedException();

        FluxoLogManager.LogError(exception, "Unable to start Fluxo.");
        FluxoLogManager.CloseAndFlush();

        var issuesDirectory = Path.Combine(_tempLocalAppData, "fluxo", "logs", "issues");
        var issueLog = ReadSingleLog(issuesDirectory, "User*.log");
        var exceptionFile = Directory
            .EnumerateFiles(issuesDirectory, "fluxo_exception_*.log")
            .Single();
        var exceptionFileName = Path.GetFileName(exceptionFile);
        var exceptionLog = File.ReadAllText(exceptionFile);

        Assert.StartsWith("fluxo_exception_", exceptionFileName);
        Assert.Matches(@"^fluxo_exception_\d{8}-\d{6}_[0-9a-f]{32}\.log$", exceptionFileName);
        Assert.Contains("Unable to start Fluxo.", issueLog);
        Assert.Contains("outer failure", exceptionLog);
        Assert.Contains("inner failure", exceptionLog);
        Assert.Contains(nameof(CreateNestedException), exceptionLog);
    }

    [Fact]
    public void FluxoLogService_LogError_CreatesOneExceptionFilePerFailure()
    {
        FluxoLogManager.Initialize("User");

        FluxoLogManager.LogError(new InvalidOperationException("first"), "First failure.");
        FluxoLogManager.LogError(new InvalidOperationException("second"), "Second failure.");
        FluxoLogManager.CloseAndFlush();

        var files = Directory.EnumerateFiles(
            Path.Combine(_tempLocalAppData, "fluxo", "logs", "issues"),
            "fluxo_exception_*.log");

        Assert.Equal(2, files.Count());
    }

    private static Exception CreateNestedException()
    {
        try
        {
            ThrowInner();
        }
        catch (Exception exception)
        {
            return new InvalidOperationException("outer failure", exception);
        }

        throw new UnreachableException();
    }

    private static void ThrowInner()
    {
        throw new InvalidOperationException("inner failure");
    }

    private static string ReadSingleLog(string directory, string pattern = "*.log")
    {
        var files = Directory.EnumerateFiles(directory, pattern)
            .Where(path => !Path.GetFileName(path).StartsWith("fluxo_exception_", StringComparison.Ordinal))
            .ToArray();

        Assert.Single(files);
        return File.ReadAllText(files[0]);
    }

    private sealed class UnreachableException : Exception;
}
