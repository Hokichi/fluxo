using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Filters;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Repositories;
using Fluxo.Services.Persistence;
using Xunit;

namespace Fluxo.Tests.Services.Persistence;

public sealed class BudgetAllocationPeriodSyncServiceTests
{
    [Fact]
    public async Task BudgetAllocationPeriodSyncService_SyncAsync_UpdatesCurrentPeriodIndex()
    {
        var unitOfWork = new TestUnitOfWork
        {
            BudgetAllocationEntity = new BudgetAllocation
            {
                AllocationPeriod = AllocationPeriod.Monthly,
                PeriodStart = 1,
                CurrentPeriodIndex = 1
            }
        };
        var service = new BudgetAllocationPeriodSyncService(() => new DateTime(2026, 6, 15));

        await service.SyncAsync(unitOfWork);

        Assert.Equal(15, unitOfWork.BudgetAllocationEntity!.CurrentPeriodIndex);
        Assert.Equal(1, unitOfWork.SaveCallCount);
    }

    [Fact]
    public async Task BudgetAllocationPeriodSyncService_SyncAsync_ClampsPeriodStartForPeriod()
    {
        var unitOfWork = new TestUnitOfWork
        {
            BudgetAllocationEntity = new BudgetAllocation
            {
                AllocationPeriod = AllocationPeriod.Quarterly,
                PeriodStart = 28
            }
        };
        var service = new BudgetAllocationPeriodSyncService(() => new DateTime(2026, 5, 1));

        await service.SyncAsync(unitOfWork);

        Assert.Equal(3, unitOfWork.BudgetAllocationEntity!.PeriodStart);
        Assert.Equal(2, unitOfWork.BudgetAllocationEntity.CurrentPeriodIndex);
    }

    [Fact]
    public async Task BudgetAllocationPeriodSyncService_SyncAsync_WhenCurrentIndexEqualsPeriodStart_DoesNotMoveRolloverPeriodMarker()
    {
        var unitOfWork = new TestUnitOfWork
        {
            BudgetAllocationEntity = new BudgetAllocation
            {
                AllocationPeriod = AllocationPeriod.Monthly,
                PeriodStart = 10,
                CurrentPeriodIndex = 10,
                LastRolloverPeriodStart = new DateTime(2026, 1, 10)
            }
        };
        var service = new BudgetAllocationPeriodSyncService(() => new DateTime(2026, 2, 10));

        await service.SyncAsync(unitOfWork);

        Assert.Equal(new DateTime(2026, 1, 10), unitOfWork.BudgetAllocationEntity!.LastRolloverPeriodStart);
        Assert.Equal(0, unitOfWork.SaveCallCount);
    }

    private sealed class TestUnitOfWork : IUnitOfWork
    {
        public ITransactionRepository Expenses => throw new NotSupportedException();
        public ITransactionRepository Transactions => throw new NotSupportedException();
        public ITagRepository Tags => throw new NotSupportedException();
        public ISavingGoalRepository SavingGoals => throw new NotSupportedException();
        public IAccountRepository Accounts => throw new NotSupportedException();
        public IRecurringTransactionRepository RecurringTransactions => throw new NotSupportedException();
        public IUserSettingsRepository UserSettings => throw new NotSupportedException();
        public IBudgetAllocationRepository BudgetAllocation => BudgetAllocationRepository;

        public int SaveCallCount { get; private set; }

        public BudgetAllocation? BudgetAllocationEntity
        {
            get => BudgetAllocationRepository.Entity;
            init => BudgetAllocationRepository.Entity = value;
        }

        private TestBudgetAllocationRepository BudgetAllocationRepository { get; } = new();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            return Task.FromResult(0);
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestBudgetAllocationRepository : IBudgetAllocationRepository
    {
        public BudgetAllocation? Entity { get; set; }

        public Task<BudgetAllocation?> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Entity);
        }

        public Task AddAsync(BudgetAllocation entity, CancellationToken cancellationToken = default)
        {
            Entity = entity;
            return Task.CompletedTask;
        }

        public void Update(BudgetAllocation entity)
        {
            Entity = entity;
        }
    }
}
