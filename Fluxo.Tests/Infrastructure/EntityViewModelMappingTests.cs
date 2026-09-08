using AutoMapper;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Mappings;
using Fluxo.ViewModels.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fluxo.Tests.Infrastructure;

public sealed class EntityViewModelMappingTests
{
    private static IMapper CreateMapper() => new MapperConfiguration(
        cfg => cfg.AddProfile<EntityViewModelProfile>(),
        NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public void TransactionMapping_PreservesNestedValuesAndSeparatesEdits()
    {
        var account = new Account
        {
            Id = 3,
            Name = "Checking",
            AccountType = AccountType.Checking,
            AccountLimit = 2000m,
            MaximumSpending = 500m,
            MinimumPayment = 25m,
            SpentAmount = 50m,
            Balance = 1500m,
            MonthlyDueDate = 12,
            DeductSource = 4,
            InterestRate = 1.5m,
            PinnedOnUI = true,
            IsEnabled = true,
            IsDefault = true
        };
        var tag = new Tag
        {
            Id = 8,
            Name = "Food",
            HexCode = "#123456",
            IsSystemTag = true,
            SpendingLimit = 250m
        };
        var source = new Transaction
        {
            Id = 42,
            Type = TransactionType.Expense,
            SourceAccountId = 3,
            Account = account,
            TagId = 8,
            Tag = tag,
            GoalId = 9,
            RepaymentAccountId = 4,
            RelatedRecurringTransactionId = 10,
            Name = "Lunch",
            Amount = 20m,
            OccurredOn = new DateTime(2026, 9, 8, 12, 30, 0),
            LoggedOn = new DateTime(2026, 9, 8, 12, 35, 0),
            Notes = "Note",
            ExpenseCategory = ExpenseCategory.Needs,
            ParentTransactionId = 41,
            IsPinned = true,
            IsForDeletion = true,
            IsIoU = true,
            ShouldAffectBalance = true
        };

        var mapper = CreateMapper();
        var vm = mapper.Map<TransactionVM>(source);

        Assert.Equal((42, TransactionType.Expense, 3, (int?)9, (int?)4),
            (vm.Id, vm.Type, vm.SourceAccountId, vm.GoalId, vm.RepaymentAccountId));
        Assert.Equal(("Lunch", 20m, source.OccurredOn, source.LoggedOn, "Note"),
            (vm.Name, vm.Amount, vm.OccurredOn, vm.LoggedOn, vm.Notes));
        Assert.Equal(ExpenseCategory.Needs, vm.ExpenseCategory);
        Assert.Equal(41, vm.ParentTransactionId);
        Assert.True(vm.IsPinned && vm.IsForDeletion && vm.IsIoU && vm.ShouldAffectBalance);
        Assert.Empty(vm.ChildTransactions);
        Assert.Equal((3, "Checking", AccountType.Checking),
            (vm.Account.Id, vm.Account.Name, vm.Account.AccountType));
        Assert.Equal((2000m, 500m, (decimal?)25m, 50m, 1500m),
            (vm.Account.AccountLimit, vm.Account.MaximumSpending,
                vm.Account.MinimumPayment, vm.Account.SpentAmount, vm.Account.Balance));
        Assert.Equal(((int?)12, (int?)4, (decimal?)1.5m),
            (vm.Account.MonthlyDueDate, vm.Account.DeductSource, vm.Account.InterestRate));
        Assert.True(vm.Account.PinnedOnUI && vm.Account.IsEnabled && vm.Account.IsDefault);
        Assert.Equal((0m, 0m), (vm.Account.MoneyIn, vm.Account.MoneyOut));
        Assert.NotNull(vm.Tag);
        Assert.Equal((8, "Food", "#123456", (decimal?)250m, true),
            (vm.Tag.Id, vm.Tag.Name, vm.Tag.HexCode, vm.Tag.SpendingLimit, vm.Tag.IsSystemTag));

        vm.Name = "Draft";
        vm.Account.Name = "Draft account";
        vm.Tag.Name = "Draft tag";
        var reloaded = mapper.Map<TransactionVM>(source);

        Assert.Equal(("Lunch", "Checking", "Food"),
            (source.Name, source.Account.Name, source.Tag!.Name));
        Assert.Equal(("Lunch", "Checking", "Food"),
            (reloaded.Name, reloaded.Account.Name, reloaded.Tag!.Name));
        Assert.NotSame(vm.Account, reloaded.Account);
        Assert.NotSame(vm.Tag, reloaded.Tag);
    }

    [Fact]
    public void RecurringMapping_PreservesGoalProgressAndOptionalNavigation()
    {
        var created = new DateTime(2026, 8, 1);
        var deadline = new DateTime(2026, 12, 1);
        var source = new RecurringTransaction
        {
            Id = 7,
            Name = "Save",
            Amount = 25m,
            Type = RecurringTransactionType.GoalUpdate,
            RecurringPeriod = RecurringPeriod.Biweekly,
            RecurringTime = 5,
            Category = ExpenseCategory.Savings,
            IsEnabled = true,
            EndDate = deadline,
            SourceId = 3,
            Source = new Account { Id = 3, Name = "Savings", AccountType = AccountType.Saving },
            GoalId = 9,
            Goal = new SavingGoal
            {
                Id = 9,
                Name = "Trip",
                CurrentAmount = 100m,
                TargetAmount = 1000m,
                CreatedOn = created,
                SavingEndDate = deadline
            }
        };

        var mapper = CreateMapper();
        var vm = mapper.Map<RecurringTransactionVM>(source);

        Assert.Equal((7, "Save", 25m, RecurringTransactionType.GoalUpdate),
            (vm.Id, vm.Name, vm.Amount, vm.Type));
        Assert.Equal((RecurringPeriod.Biweekly, 5), (vm.RecurringPeriod, vm.RecurringTime));
        Assert.Equal(ExpenseCategory.Savings, vm.Category);
        Assert.True(vm.IsEnabled);
        Assert.Equal(deadline, vm.EndDate);
        Assert.Equal((3, "Savings", AccountType.Saving),
            (vm.Source.Id, vm.Source.Name, vm.Source.AccountType));
        Assert.Null(vm.Tag);
        Assert.NotNull(vm.Goal);
        Assert.Equal((9, "Trip", 100m, 1000m),
            (vm.Goal.Id, vm.Goal.Name, vm.Goal.CurrentAmount, vm.Goal.TargetAmount));
        Assert.Equal(created, vm.Goal.CreatedOn);
        Assert.Equal(deadline, vm.Goal.SavingEndDate);
        Assert.Equal(900m, vm.Goal.RemainingAmount);
        Assert.Equal(0.1m, vm.Goal.ProgressRatio);
        vm.Goal.CurrentAmount = 900m;
        Assert.Equal(100m, source.Goal.CurrentAmount);
        source.Goal = null;
        Assert.Null(mapper.Map<RecurringTransactionVM>(source).Goal);
        Assert.Null(mapper.Map<TransactionVM>(new Transaction { Account = source.Source }).Tag);
        Assert.Empty(mapper.Map<IReadOnlyList<TagVM>>(Array.Empty<Tag>()));
    }
}
