using Fluxo.Core.Entities;
using Xunit;

namespace Fluxo.Tests.Services.Caching;

public sealed class AppDataCommitCoordinatorTests
{
    [Fact]
    public async Task SaveChangesAsync_PublishesOnlyAfterDatabaseCommit()
    {
        await using var fixture = await AppDataCommitCoordinatorFixture.CreateAsync();
        var appData = fixture.CreateAppDataService();
        var account = new Account { Name = "Main", IsEnabled = true };
        await appData.AddAccountAsync(account);

        Assert.Empty(await appData.GetAccountsAsync());
        Assert.Empty(await fixture.ReadDatabaseAccountsAsync());
        var previousVersion = fixture.Cache.Version;

        await appData.SaveChangesAsync();

        Assert.True(account.Id > 0);
        Assert.Equal(previousVersion + 1, fixture.Cache.Version);
        Assert.Equal("Main", Assert.Single(await appData.GetAccountsAsync()).Name);
        Assert.Equal("Main", Assert.Single(await fixture.ReadDatabaseAccountsAsync()).Name);
    }

    [Fact]
    public async Task SaveChangesAsync_MultipleMutationsPublishOneVersion()
    {
        await using var fixture = await AppDataCommitCoordinatorFixture.CreateAsync();
        var appData = fixture.CreateAppDataService();
        await appData.AddAccountAsync(new Account { Name = "One", IsEnabled = true });
        await appData.AddAccountAsync(new Account { Name = "Two", IsEnabled = true });
        var previousVersion = fixture.Cache.Version;

        await appData.SaveChangesAsync();

        Assert.Equal(previousVersion + 1, fixture.Cache.Version);
        Assert.Equal(2, (await appData.GetAccountsAsync()).Count);
    }

    [Fact]
    public async Task ConcurrentScopedSaves_DoNotLoseCacheUpdates()
    {
        await using var fixture = await AppDataCommitCoordinatorFixture.CreateAsync();
        var first = fixture.CreateAppDataService();
        var second = fixture.CreateAppDataService();
        await first.AddAccountAsync(new Account { Name = "One", IsEnabled = true });
        await second.AddAccountAsync(new Account { Name = "Two", IsEnabled = true });

        await Task.WhenAll(first.SaveChangesAsync(), second.SaveChangesAsync());

        Assert.Equal(3, fixture.Cache.Version);
        Assert.Equal(["One", "Two"],
            (await fixture.Cache.GetAccountsAsync()).Select(account => account.Name).Order().ToArray());
    }

    [Fact]
    public async Task SaveInProgress_DoesNotBlockReaderAndReaderSeesCommittedSnapshot()
    {
        var blocker = new BlockingSaveChangesInterceptor();
        await using var fixture = await AppDataCommitCoordinatorFixture.CreateAsync(blocker);
        var appData = fixture.CreateAppDataService();
        await appData.AddAccountAsync(new Account { Name = "Pending", IsEnabled = true });
        blocker.IsEnabled = true;

        var save = appData.SaveChangesAsync();
        await blocker.Entered.WaitAsync(TimeSpan.FromSeconds(5));
        var read = await fixture.Cache.GetAccountsAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Empty(read);
        blocker.Release();
        await save;
        Assert.Equal("Pending", Assert.Single(await fixture.Cache.GetAccountsAsync()).Name);
    }
}
