using Fluxo.Core.Entities;
using Fluxo.Services.Transactions;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Repositories;
using Fluxo.Services.Persistence;
using Fluxo.ViewModels.Popups;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class GoalUpdateTransactionSupportTests
{
    [Fact]
    public async Task GoalUpdateTransactionSupport_ResolveGoalUpdateTagAsync_ReturnsExistingTag_WhenItAlreadyExists()
    {
        var existingTag = new Tag
        {
            Id = 42,
            Name = "Goal Update",
            HexCode = "#ffffff"
        };

        var tagRepository = new TestTagRepository([existingTag]);
        var unitOfWork = new TestUnitOfWork(tagRepository);

        var resolvedTag = await GoalUpdateTransactionSupport.ResolveGoalUpdateTagAsync(new AppDataService(unitOfWork));

        Assert.Equal(existingTag.Id, resolvedTag.Id);
        Assert.Equal(1, tagRepository.Tags.Count);
        Assert.Equal(0, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task GoalUpdateTransactionSupport_ResolveGoalUpdateTagAsync_CreatesGoalUpdateTag_WhenMissing()
    {
        var tagRepository = new TestTagRepository([]);
        var unitOfWork = new TestUnitOfWork(tagRepository);

        var resolvedTag = await GoalUpdateTransactionSupport.ResolveGoalUpdateTagAsync(new AppDataService(unitOfWork));

        Assert.Equal("Goal Update", resolvedTag.Name);
        Assert.Equal("#aed4e1", resolvedTag.HexCode);
        Assert.Single(tagRepository.Tags);
        Assert.Equal(1, unitOfWork.SaveChangesCalls);
    }

    private sealed class TestUnitOfWork(TestTagRepository tags) : IUnitOfWork
    {
        public int SaveChangesCalls { get; private set; }

        public ITransactionRepository Expenses => throw new NotSupportedException();
        public ITransactionRepository Transactions => throw new NotSupportedException();
        public ITagRepository Tags => tags;
        public ISavingGoalRepository SavingGoals => throw new NotSupportedException();
        public IAccountRepository Accounts => throw new NotSupportedException();
        public IRecurringTransactionRepository RecurringTransactions => throw new NotSupportedException();
        public IUserSettingsRepository UserSettings => throw new NotSupportedException();
        public IBudgetAllocationRepository BudgetAllocation => throw new NotSupportedException();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls++;
            return Task.FromResult(1);
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestTagRepository(IReadOnlyList<Tag> initialTags) : ITagRepository
    {
        private int _nextId = initialTags.Count == 0 ? 1 : initialTags.Max(tag => tag.Id) + 1;

        public List<Tag> Tags { get; } = [.. initialTags];

        public Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Tag>>(Tags);
        }

        public Task<Tag?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Tags.FirstOrDefault(tag => tag.Id == id));
        }

        public Task AddAsync(Tag entity, CancellationToken cancellationToken = default)
        {
            if (entity.Id <= 0)
                entity.Id = _nextId++;

            Tags.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(Tag entity)
        {
            var index = Tags.FindIndex(tag => tag.Id == entity.Id);
            if (index >= 0)
                Tags[index] = entity;
        }

        public void Remove(Tag entity)
        {
            Tags.RemoveAll(tag => tag.Id == entity.Id);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }

        public Task<IReadOnlyList<(Tag Tag, int Count)>> GetTagsByCountDescendingAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<(Tag Tag, int Count)>>([]);
        }

        public Task<IReadOnlyList<(Tag Tag, int Count)>> GetTodayTagsByCountDescendingAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<(Tag Tag, int Count)>>([]);
        }
    }
}
