using Xunit;

namespace Fluxo.Tests.Infrastructure;

public sealed class DatabaseDirectoryPathTests
{
    [Fact]
    public void DatabaseDirectoryPath_DatabasePath_AppendsFluxoDatabaseFileNameToDatabaseDirectory()
    {
        var databasePath = Fluxo.Data.Context.FluxoDbContextFactory.GetDatabasePath();
        var directoryPath = Fluxo.Data.Context.FluxoDbContextFactory.GetDatabaseDirectoryPath();

        Assert.Equal(Path.Combine(directoryPath, "fluxo.db"), databasePath);
    }
}
