using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Repositories;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Extensions;
using Fluxo.Tests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Infrastructure;

public sealed class AppDataServiceLifetimeTests
{
    [Fact]
    public async Task AppDataServiceLifetime_ScopedServices_SaveWritesQueuedByAnotherResolution()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var settings = new InMemoryUserSettingsRepository();
        unitOfWork.UserSettings.Returns(settings);
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));

        using var services = new ServiceCollection()
            .AddSingleton<IDataOperationRunner>(new InlineDataOperationRunner(unitOfWork))
            .AddFluxoPresentation()
            .BuildServiceProvider();
        using var scope = services.CreateScope();

        var writer = scope.ServiceProvider.GetRequiredService<IAppDataService>();
        var saver = scope.ServiceProvider.GetRequiredService<IAppDataService>();
        await writer.AddUserSettingAsync(new UserSettings { Name = "theme", Value = "dark" });
        await saver.SaveChangesAsync();

        var persisted = await saver.GetUserSettingByNameAsync("theme");
        Assert.Equal("dark", persisted?.Value);
    }

    private sealed class InMemoryUserSettingsRepository : IUserSettingsRepository
    {
        private readonly Dictionary<string, UserSettings> _settings = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<UserSettings>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserSettings>>(_settings.Values.ToList());

        public Task<UserSettings?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_settings.TryGetValue(name, out var setting) ? setting : null);

        public Task AddAsync(UserSettings entity, CancellationToken cancellationToken = default)
        {
            _settings.Add(entity.Name, entity);
            return Task.CompletedTask;
        }

        public void Update(UserSettings entity) => _settings[entity.Name] = entity;

        public void Remove(UserSettings entity) => _settings.Remove(entity.Name);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }
}
