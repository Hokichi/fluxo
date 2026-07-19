using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using AutoMapper;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Repositories;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Services.Persistence;
using Fluxo.Tests.TestDoubles;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Shell.Main;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class TransactionPopupVMValidationTests
{
    [Fact]
    public void UnpostedIoU_ForcesBudgetExclusion_AndRegularModeClearsIt()
    {
        RunInSta(() =>
        {
            var vm = new TransactionPopupVM(
                CreateMainViewModel([CreateCheckingSource(balance: 500m)]),
                CreateAppData());

            vm.IsUnpostedIoUMode = true;

            Assert.True(vm.IsBudgetExcluded);
            Assert.False(vm.CanToggleBudgetExclusion);

            vm.IsRegularMode = true;

            Assert.False(vm.IsBudgetExcluded);
            Assert.True(vm.CanToggleBudgetExclusion);
        });
    }

    [Fact]
    public void Constructor_UsesAddNewTransactionPurposeByDefault()
    {
        RunInSta(() =>
        {
            var vm = new TransactionPopupVM(CreateMainViewModel([CreateCheckingSource(balance: 500m)]), CreateAppData());

            Assert.Equal("Add New Transaction", vm.PopupTitle);
            Assert.True(vm.CanChangeTransactionType);
            Assert.True(vm.CanPinTransaction);
            Assert.True(vm.ShowHistoryPanel);
        });
    }

    [Fact]
    public void PostedIoU_ForcesBudgetExclusion()
    {
        RunInSta(() =>
        {
            var vm = new TransactionPopupVM(
                CreateMainViewModel([CreateCheckingSource(balance: 500m)]),
                CreateAppData());

            vm.IsPostedIoUMode = true;

            Assert.True(vm.IsBudgetExcluded);
            Assert.False(vm.CanToggleBudgetExclusion);
        });
    }

    [Fact]
    public void Constructor_WithCreditAccount_LeavesNameEmpty()
    {
        RunInSta(() =>
        {
            var checking = CreateCheckingSource(balance: 500m);
            var credit = new AccountVM
            {
                Id = 2,
                Name = "Visa",
                AccountType = AccountType.Credit,
                IsEnabled = true
            };

            var vm = new TransactionPopupVM(
                CreateMainViewModel([checking, credit]),
                CreateAppData());

            Assert.Empty(vm.NameText);
        });
    }

    [Fact]
    public void InitializeRecurringMode_UsesRecurringCreatePurposeAndDisablesPin()
    {
        RunInSta(() =>
        {
            var vm = new TransactionPopupVM(CreateMainViewModel([CreateCheckingSource(balance: 500m)]), CreateAppData());

            vm.InitializeRecurringMode(isLocked: false);

            Assert.Equal("Add Recurring Transaction", vm.PopupTitle);
            Assert.True(vm.CanChangeTransactionType);
            Assert.False(vm.CanPinTransaction);
        });
    }

    [Fact]
    public void InitializeRepayment_FiltersSources_DefaultsTargetAndLocksRequestedTarget()
    {
        RunInSta(() =>
        {
            var checking = new AccountVM
            {
                Id = 1,
                Name = "Checking",
                AccountType = AccountType.Checking,
                IsEnabled = true
            };
            var cash = new AccountVM
            {
                Id = 2,
                Name = "Cash",
                AccountType = AccountType.Cash,
                IsEnabled = true
            };
            var credit = new AccountVM
            {
                Id = 3,
                Name = "Visa",
                AccountType = AccountType.Credit,
                IsEnabled = true,
                SpentAmount = 90m,
                DeductSource = checking.Id
            };
            var vm = new TransactionPopupVM(
                CreateMainViewModel([checking, cash, credit]),
                CreateAppData());

            vm.InitializeRepayment(credit);

            Assert.True(vm.IsRepayment);
            Assert.Equal(new[] { checking.Id }, vm.Accounts.Select(account => account.Id));
            Assert.Equal(credit.Id, vm.SelectedRepaymentAccount?.Id);
            Assert.False(vm.CanChangeRepaymentAccount);
            Assert.Equal(credit.SpentAmount, vm.AmountText);
            Assert.Equal("Repayment to Visa", vm.NameText);
            Assert.False(vm.CanToggleRecurring);
            Assert.False(vm.CanPinTransaction);
            Assert.False(vm.CanEditTransactionName);
            Assert.False(vm.CanEditTags);
            Assert.False(vm.ShowNoteField);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SwitchingToRepayment_GeneratesNameAndDisablesEditing(bool startFromGoalUpdate)
    {
        RunInSta(() =>
        {
            var checking = CreateCheckingSource(balance: 500m);
            var credit = new AccountVM
            {
                Id = 2,
                Name = "Visa",
                AccountType = AccountType.Credit,
                SpentAmount = 80m,
                IsEnabled = true
            };
            var vm = new TransactionPopupVM(
                CreateMainViewModel([checking, credit]),
                CreateAppData());
            if (startFromGoalUpdate)
                vm.IsGoal = true;

            vm.IsRepayment = true;

            Assert.Equal("Repayment to Visa", vm.NameText);
            Assert.Equal(80m, vm.AmountText);
            Assert.False(vm.CanEditTransactionName);
        });
    }

    [Fact]
    public void ChangingRepaymentAccount_RefreshesGeneratedName()
    {
        RunInSta(() =>
        {
            var checking = CreateCheckingSource(balance: 500m);
            var visa = new AccountVM
            {
                Id = 2,
                Name = "Visa",
                AccountType = AccountType.Credit,
                SpentAmount = 40m,
                IsEnabled = true
            };
            var mastercard = new AccountVM
            {
                Id = 3,
                Name = "Mastercard",
                AccountType = AccountType.Credit,
                SpentAmount = 120m,
                IsEnabled = true
            };
            var vm = new TransactionPopupVM(
                CreateMainViewModel([checking, visa, mastercard]),
                CreateAppData());
            vm.IsRepayment = true;

            vm.SelectedRepaymentAccount = mastercard;

            Assert.Equal("Repayment to Mastercard", vm.NameText);
            Assert.Equal(120m, vm.AmountText);
        });
    }

    [Fact]
    public void RejectRepaymentOverpayment_MarksAmountInvalid()
    {
        RunInSta(() =>
        {
            var vm = CreateRepaymentVm(spentAmount: 50m);
            vm.AmountText = 60m;

            Assert.True(vm.TryGetRepaymentCorrection(out var corrected));
            Assert.Equal(50m, corrected);

            vm.RejectRepaymentCorrection();

            Assert.Equal("Invalid Repayment", vm.AmountValidationHint);
            Assert.False(vm.CanSave);
        });
    }

    [Fact]
    public void AcceptRepaymentOverpayment_UsesSpentAmountAndClearsError()
    {
        RunInSta(() =>
        {
            var vm = CreateRepaymentVm(spentAmount: 50m);
            vm.AmountText = 60m;
            vm.RejectRepaymentCorrection();

            vm.AcceptRepaymentCorrection();

            Assert.Equal(50m, vm.AmountText);
            Assert.NotEqual("Invalid Repayment", vm.AmountValidationHint);
        });
    }

    [Fact]
    public void SaveAsync_Repayment_CreatesExcludedBalanceUpdatePair()
    {
        RunInSta(() =>
        {
            var checkingVm = CreateCheckingSource(balance: 500m);
            var creditVm = new AccountVM
            {
                Id = 2,
                Name = "Visa",
                AccountType = AccountType.Credit,
                IsEnabled = true,
                SpentAmount = 200m,
                DeductSource = checkingVm.Id
            };
            var checking = new Account
            {
                Id = checkingVm.Id,
                Name = checkingVm.Name,
                AccountType = AccountType.Checking,
                Balance = checkingVm.Balance
            };
            var credit = new Account
            {
                Id = creditVm.Id,
                Name = creditVm.Name,
                AccountType = AccountType.Credit,
                SpentAmount = creditVm.SpentAmount
            };
            var saved = new List<Transaction>();
            var appData = CreateAppData();
            appData.GetAccountByIdAsync(checking.Id, Arg.Any<CancellationToken>()).Returns(checking);
            appData.GetAccountByIdAsync(credit.Id, Arg.Any<CancellationToken>()).Returns(credit);
            appData.GetTagsAsync(Arg.Any<CancellationToken>()).Returns(
            [
                new Tag { Id = 9, Name = "Balance Update", HexCode = "#fff", IsSystemTag = true }
            ]);
            appData.AddTransactionAsync(
                    Arg.Do<Transaction>(transaction => saved.Add(transaction)),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            var vm = new TransactionPopupVM(
                CreateMainViewModel([checkingVm, creditVm]),
                appData);
            vm.InitializeRepayment(creditVm);
            vm.AmountText = 75m;

            var result = vm.SaveAsync(false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(425m, checking.Balance);
            Assert.Equal(125m, credit.SpentAmount);
            Assert.Equal(2, saved.Count);
            var expense = Assert.Single(saved, transaction => transaction.Type == TransactionType.Expense);
            Assert.Equal(9, expense.TagId);
            Assert.True(expense.IsExcludedFromBudget);
            Assert.Single(saved, transaction => transaction.Type == TransactionType.Income);
        });
    }

    [Fact]
    public void SaveAsync_Repayment_CreatesBalanceUpdateTag_WhenMissing()
    {
        RunInSta(() =>
        {
            var checkingVm = CreateCheckingSource(balance: 500m);
            var creditVm = new AccountVM
            {
                Id = 2,
                Name = "Visa",
                AccountType = AccountType.Credit,
                IsEnabled = true,
                SpentAmount = 200m,
                DeductSource = checkingVm.Id
            };
            var checking = new Account
            {
                Id = checkingVm.Id,
                Name = checkingVm.Name,
                AccountType = AccountType.Checking,
                Balance = checkingVm.Balance
            };
            var credit = new Account
            {
                Id = creditVm.Id,
                Name = creditVm.Name,
                AccountType = AccountType.Credit,
                SpentAmount = creditVm.SpentAmount
            };
            var saved = new List<Transaction>();
            Tag? addedTag = null;
            var appData = CreateAppData();
            appData.GetAccountByIdAsync(checking.Id, Arg.Any<CancellationToken>()).Returns(checking);
            appData.GetAccountByIdAsync(credit.Id, Arg.Any<CancellationToken>()).Returns(credit);
            appData.GetTagsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Tag>>([]));
            appData.AddTagAsync(
                    Arg.Do<Tag>(tag =>
                    {
                        tag.Id = 9;
                        addedTag = tag;
                    }),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            appData.AddTransactionAsync(
                    Arg.Do<Transaction>(transaction => saved.Add(transaction)),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            var vm = new TransactionPopupVM(
                CreateMainViewModel([checkingVm, creditVm]),
                appData);
            vm.InitializeRepayment(creditVm);
            vm.AmountText = 75m;

            var result = vm.SaveAsync(false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.NotNull(addedTag);
            Assert.Equal(SystemTags.BalanceUpdateName, addedTag!.Name);
            Assert.Equal(SystemTags.BalanceUpdateHexCode, addedTag.HexCode);
            var expense = Assert.Single(saved, transaction => transaction.Type == TransactionType.Expense);
            Assert.Equal(9, expense.TagId);
        });
    }

    [Fact]
    public void InitializeFromRecurringTransaction_UsesEditPurposeAndLocksTransactionType()
    {
        RunInSta(() =>
        {
            var recurring = new RecurringTransaction
            {
                Id = 42,
                Name = "Rent",
                Amount = 120m,
                Type = RecurringTransactionType.Expense,
                SourceId = 1,
                TagId = 1,
                Category = ExpenseCategory.Needs,
                RecurringPeriod = RecurringPeriod.Monthly,
                RecurringTime = 5,
                IsEnabled = true
            };
            var appData = CreateAppData();
            appData.GetRecurringTransactionByIdAsync(42, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<RecurringTransaction?>(recurring));
            var vm = new TransactionPopupVM(CreateMainViewModel([CreateCheckingSource(balance: 500m)]), appData);

            var initialized = vm.InitializeFromRecurringTransactionAsync(42).GetAwaiter().GetResult();

            Assert.True(initialized);
            Assert.Equal("Edit Recurring Transaction", vm.PopupTitle);
            Assert.False(vm.CanChangeTransactionType);
            Assert.False(vm.CanPinTransaction);
            Assert.Equal(ExpenseCategory.Needs, vm.SelectedExpenseCategory);
        });
    }

    [Fact]
    public void SwitchingToGoalUpdate_AutoSetsNameFromSelectedGoal()
    {
        RunInSta(() =>
        {
            var vm = new TransactionPopupVM(CreateMainViewModel([CreateCheckingSource(balance: 500m)]), CreateAppData());

            vm.IsGoal = true;

            Assert.Equal("Goal Update for Goal", vm.NameText);
        });
    }

    [Fact]
    public void ChangingSelectedGoal_RefreshesGoalUpdateName()
    {
        RunInSta(() =>
        {
            var vm = new TransactionPopupVM(CreateMainViewModel([CreateCheckingSource(balance: 500m)]), CreateAppData());
            vm.IsGoal = true;

            vm.SelectedGoal = new SavingGoalVM { Id = 2, Name = "Emergency Fund" };

            Assert.Equal("Goal Update for Emergency Fund", vm.NameText);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SwitchingFromGoalUpdateToRegularMode_ClearsName(bool switchToExpense)
    {
        RunInSta(() =>
        {
            var vm = new TransactionPopupVM(
                CreateMainViewModel([CreateCheckingSource(balance: 500m)]),
                CreateAppData());
            vm.IsGoal = true;

            if (switchToExpense)
                vm.IsExpense = true;
            else
                vm.IsIncome = true;

            Assert.Empty(vm.NameText);
        });
    }

    [Theory]
    [InlineData(true, "Repayment to Visa")]
    [InlineData(true, "Manually assigned")]
    [InlineData(false, "Repayment to Visa")]
    [InlineData(false, "Manually assigned")]
    public void SwitchingFromRepaymentToRegularMode_ClearsName(
        bool switchToExpense,
        string repaymentName)
    {
        RunInSta(() =>
        {
            var vm = CreateRepaymentVm(spentAmount: 50m);
            vm.NameText = repaymentName;

            if (switchToExpense)
                vm.IsExpense = true;
            else
                vm.IsIncome = true;

            Assert.Empty(vm.NameText);
        });
    }

    [Fact]
    public void InitializeFromDraft_LockedGoalUpdateLocksTypeAndKeepsGoalSelectionEditable()
    {
        RunInSta(() =>
        {
            var main = CreateMainViewModel([CreateCheckingSource(balance: 500m)]);
            main.SavingGoalsPanel.SavingGoals.Add(new SavingGoalVM
            {
                Id = 2,
                Name = "Emergency Fund",
                TargetAmount = 1_000m,
                CurrentAmount = 100m
            });
            var vm = new TransactionPopupVM(main, CreateAppData());

            vm.InitializeFromDraft(new TransactionPopupVM.TransactionPopupDraft(
                IsExpense: false,
                Name: string.Empty,
                AmountText: 0m,
                AccountId: null,
                Date: DateTime.Today,
                Note: string.Empty,
                Category: null,
                TagId: null,
                IsGoal: true,
                GoalId: 2,
                LockTransactionType: true));

            Assert.False(vm.CanChangeTransactionType);
            Assert.Equal(2, vm.SelectedGoal?.Id);
            Assert.Equal("Goal Update for Emergency Fund", vm.NameText);

            vm.SelectedGoal = vm.Goals.Single(goal => goal.Id == 1);

            Assert.Equal("Goal Update for Goal", vm.NameText);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Expense_Name_IsRequired(bool isRecurring)
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Expense, CreateCheckingSource(balance: 500m), isRecurring: isRecurring);
            vm.NameText = " ";

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.False(result.IsSuccess);
            Assert.Equal("Please enter a name.", result.ErrorMessage);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Income_Name_IsRequired(bool isRecurring)
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Income, CreateCheckingSource(balance: 0m), isRecurring: isRecurring);
            vm.NameText = " ";

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.False(result.IsSuccess);
            Assert.Equal("Please enter a name.", result.ErrorMessage);
        });
    }

    [Fact]
    public void GoalUpdate_Name_IsNotRequired()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Goal, CreateCheckingSource(balance: 500m), isRecurring: false);
            vm.NameText = " ";

            Assert.False(vm.HasErrors);
            Assert.True(vm.CanSave);
        });
    }

    [Fact]
    public void Constructor_DoesNotValidateNameOrAmountBeforeFieldsLoseFocus()
    {
        RunInSta(() =>
        {
            var vm = new TransactionPopupVM(CreateMainViewModel([CreateCheckingSource(balance: 500m)]), CreateAppData());

            Assert.False(vm.HasErrors);
            Assert.False(vm.CanSave);
            Assert.Equal(string.Empty, vm.NameValidationHint);
            Assert.Equal(string.Empty, vm.AmountValidationHint);
        });
    }

    [Fact]
    public void CanSave_BecomesEnabled_WhenInitialRequiredFieldsAreValid()
    {
        RunInSta(() =>
        {
            var vm = new TransactionPopupVM(CreateMainViewModel([CreateCheckingSource(balance: 500m)]), CreateAppData());

            vm.NameText = "Coffee";
            vm.AmountText = 5m;

            Assert.True(vm.CanSave);
            Assert.False(vm.HasErrors);
            Assert.Equal(string.Empty, vm.NameValidationHint);
            Assert.Equal(string.Empty, vm.AmountValidationHint);
        });
    }

    [Fact]
    public void ValidateAmountField_DoesNotValidateNameField()
    {
        RunInSta(() =>
        {
            var vm = new TransactionPopupVM(CreateMainViewModel([CreateCheckingSource(balance: 500m)]), CreateAppData());

            vm.ValidateAmountField();

            Assert.Empty(vm.GetErrors(nameof(TransactionPopupVM.NameText)));
            Assert.Contains(vm.GetErrors(nameof(TransactionPopupVM.AmountText)), error => error.ErrorMessage == "Please enter a valid amount greater than zero.");
            Assert.Equal(string.Empty, vm.NameValidationHint);
            Assert.Equal("Invalid Amount", vm.AmountValidationHint);
        });
    }

    [Fact]
    public void ActivateAmountValidation_ActivatesOnceAndAmountChangesRevalidate()
    {
        RunInSta(() =>
        {
            var vm = new TransactionPopupVM(CreateMainViewModel([CreateCheckingSource(balance: 500m)]), CreateAppData());
            var amountErrorsChangedCount = 0;
            vm.ErrorsChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TransactionPopupVM.AmountText))
                    amountErrorsChangedCount++;
            };

            vm.ActivateAmountValidation();
            var countAfterActivation = amountErrorsChangedCount;

            Assert.Contains(vm.GetErrors(nameof(TransactionPopupVM.AmountText)), error => error.ErrorMessage == "Please enter a valid amount greater than zero.");
            Assert.Equal("Invalid Amount", vm.AmountValidationHint);

            vm.ActivateAmountValidation();
            Assert.Equal(countAfterActivation, amountErrorsChangedCount);

            vm.AmountText = 10m;

            Assert.Empty(vm.GetErrors(nameof(TransactionPopupVM.AmountText)));
            Assert.Equal(string.Empty, vm.AmountValidationHint);
        });
    }

    [Fact]
    public void AmountValidation_ForExpenseWarnsWhenTagSpendingLimitWouldBeExceeded()
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m);
            var appData = CreateAppData(expenseLogs:
            [
                CreateTransaction("Groceries", 90m, tagId: 1, sourceId: source.Id)
            ]);
            var vm = CreateVm(TransactionKind.Expense, source, isRecurring: false, amount: 5m, appData: appData);
            vm.SelectedTag = new TagVM
            {
                Id = 1,
                Name = "General",
                HexCode = "#22C55E",
                SpendingLimit = 100m
            };
            vm.SelectedDate = DateTime.Today;
            vm.ValidateAmountField();

            vm.AmountText = 15m;

            Assert.Contains(vm.GetErrors(nameof(TransactionPopupVM.AmountText)),
                error => error.ErrorMessage == "General spending limit exceeded.");
        });
    }

    [Fact]
    public void AmountValidation_ForExpenseClearsTagLimitErrorWhenAmountFitsLimit()
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m);
            var appData = CreateAppData(expenseLogs:
            [
                CreateTransaction("Groceries", 90m, tagId: 1, sourceId: source.Id)
            ]);
            var vm = CreateVm(TransactionKind.Expense, source, isRecurring: false, amount: 15m, appData: appData);
            vm.SelectedTag = new TagVM
            {
                Id = 1,
                Name = "General",
                HexCode = "#22C55E",
                SpendingLimit = 100m
            };
            vm.SelectedDate = DateTime.Today;
            vm.ValidateAmountField();

            vm.AmountText = 5m;

            Assert.DoesNotContain(vm.GetErrors(nameof(TransactionPopupVM.AmountText)),
                error => error.ErrorMessage == "General spending limit exceeded.");
        });
    }

    [Fact]
    public void AmountValidation_ForExcludedExpense_IgnoresTagSpendingLimit()
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m);
            var appData = CreateAppData(expenseLogs:
            [
                CreateTransaction("Groceries", 90m, tagId: 1, sourceId: source.Id)
            ]);
            var vm = CreateVm(TransactionKind.Expense, source, isRecurring: false, amount: 15m, appData: appData);
            vm.SelectedTag = new TagVM
            {
                Id = 1,
                Name = "General",
                HexCode = "#22C55E",
                SpendingLimit = 100m
            };
            vm.IsExcludedFromBudget = true;

            vm.ValidateAmountField();

            Assert.DoesNotContain(vm.GetErrors(nameof(TransactionPopupVM.AmountText)),
                error => error.ErrorMessage == "General spending limit exceeded.");
        });
    }

    [Fact]
    public void AmountWarning_UsesSelectedDateAndIgnoresExcludedTransactions()
    {
        RunInSta(() =>
        {
            var selectedDate = new DateTime(2026, 7, 1);
            var included = CreateTransaction("Lunch", 8m, sourceId: 1);
            included.OccurredOn = selectedDate;
            var excluded = CreateTransaction("Transfer", 100m, sourceId: 1);
            excluded.OccurredOn = selectedDate;
            excluded.IsExcludedFromBudget = true;
            var otherDay = CreateTransaction("Yesterday", 100m, sourceId: 1);
            otherDay.OccurredOn = selectedDate.AddDays(-1);
            var allocation = new BudgetAllocation
            {
                AllocationLimit = 70m,
                AllocationPeriod = AllocationPeriod.Weekly
            };
            var vm = CreateVm(
                TransactionKind.Expense,
                CreateCheckingSource(balance: 500m),
                isRecurring: false,
                amount: 3m,
                appData: CreateAppData(allocation, [included, excluded, otherDay]));
            vm.SelectedDate = selectedDate;

            Assert.Equal("Over Daily Allowance", vm.AmountWarningHint);
            Assert.True(vm.CanSave);
        });
    }

    [Fact]
    public void AmountWarning_ExcludedCandidate_HasNoWarning()
    {
        RunInSta(() =>
        {
            var selectedDate = new DateTime(2026, 7, 1);
            var included = CreateTransaction("Lunch", 10m, sourceId: 1);
            included.OccurredOn = selectedDate;
            var allocation = new BudgetAllocation
            {
                AllocationLimit = 70m,
                AllocationPeriod = AllocationPeriod.Weekly
            };
            var vm = CreateVm(
                TransactionKind.Expense,
                CreateCheckingSource(balance: 500m),
                isRecurring: false,
                amount: 1m,
                appData: CreateAppData(allocation, [included]));
            vm.SelectedDate = selectedDate;
            vm.IsExcludedFromBudget = true;

            Assert.Equal(string.Empty, vm.AmountWarningHint);
        });
    }

    [Fact]
    public void ValidateNameField_DoesNotValidateAmountField()
    {
        RunInSta(() =>
        {
            var vm = new TransactionPopupVM(CreateMainViewModel([CreateCheckingSource(balance: 500m)]), CreateAppData());

            vm.ValidateNameField();

            Assert.Contains(vm.GetErrors(nameof(TransactionPopupVM.NameText)), error => error.ErrorMessage == "Please enter a name.");
            Assert.Empty(vm.GetErrors(nameof(TransactionPopupVM.AmountText)));
            Assert.Equal("Required", vm.NameValidationHint);
            Assert.Equal(string.Empty, vm.AmountValidationHint);
        });
    }

    [Fact]
    public void TransactionModeChange_ClearsNameValidation()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Expense, CreateCheckingSource(balance: 500m), isRecurring: false);
            vm.NameText = " ";
            vm.ValidateNameField();

            Assert.True(vm.HasErrors);
            Assert.Equal("Required", vm.NameValidationHint);

            vm.IsIncome = true;

            Assert.Empty(vm.GetErrors(nameof(TransactionPopupVM.NameText)));
            Assert.Equal(string.Empty, vm.NameValidationHint);
        });
    }

    [Fact]
    public void AmountValidation_WhenActive_RevalidatesWhenAccountChanges()
    {
        RunInSta(() =>
        {
            var lowBalance = CreateCheckingSource(balance: 20m);
            var highBalance = new AccountVM
            {
                Id = 2,
                Name = "Checking 2",
                AccountType = AccountType.Checking,
                Balance = 100m,
                IsEnabled = true
            };
            var vm = new TransactionPopupVM(CreateMainViewModel([lowBalance, highBalance]), CreateAppData());
            vm.SelectedAccount = lowBalance;
            vm.AmountText = 30m;
            vm.ValidateAmountField();

            Assert.Equal("Insufficient Balance", vm.AmountValidationHint);

            vm.SelectedAccount = highBalance;

            Assert.Empty(vm.GetErrors(nameof(TransactionPopupVM.AmountText)));
            Assert.Equal(string.Empty, vm.AmountValidationHint);
        });
    }

    [Fact]
    public void HasChanges_TracksEntityChanges()
    {
        RunInSta(() =>
        {
            var vm = new TransactionPopupVM(CreateMainViewModel([CreateCheckingSource(balance: 500m)]), CreateAppData());
            vm.BeginChangeTracking();

            vm.SelectedDate = vm.SelectedDate.AddDays(1);
            vm.NoteText = "Ignore for pending updates";
            vm.IsRecurring = true;

            Assert.True(vm.HasChanges);

            vm.NameText = "Coffee";

            Assert.True(vm.HasChanges);
        });
    }

    [Fact]
    public void AmountValidationHint_MapsSpendingCapacityFailuresToShortText()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Expense, CreateCheckingSource(balance: 20m), isRecurring: false, amount: 30m);

            vm.ValidateAmountField();

            Assert.Equal("Insufficient Balance", vm.AmountValidationHint);
        });
    }

    [Theory]
    [InlineData(TransactionKind.Expense)]
    [InlineData(TransactionKind.Income)]
    public void Name_RejectsControlCharacters(TransactionKind kind)
    {
        RunInSta(() =>
        {
            var vm = CreateVm(kind, CreateCheckingSource(balance: 500m), isRecurring: false);
            vm.NameText = "Bad\u0001Name";

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.False(result.IsSuccess);
            Assert.Equal("Name cannot contain control characters.", result.ErrorMessage);
        });
    }

    [Theory]
    [InlineData(TransactionKind.Expense)]
    [InlineData(TransactionKind.Income)]
    public void Name_RejectsLengthOver256(TransactionKind kind)
    {
        RunInSta(() =>
        {
            var vm = CreateVm(kind, CreateCheckingSource(balance: 500m), isRecurring: false);
            vm.NameText = new string('a', 257);

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.False(result.IsSuccess);
            Assert.Equal("Name cannot exceed 256 characters.", result.ErrorMessage);
        });
    }

    [Theory]
    [InlineData(TransactionKind.Expense, false)]
    [InlineData(TransactionKind.Expense, true)]
    [InlineData(TransactionKind.Income, false)]
    [InlineData(TransactionKind.Income, true)]
    [InlineData(TransactionKind.Goal, false)]
    [InlineData(TransactionKind.Goal, true)]
    public void Amount_Zero_IsInvalid_ForAllTypes(TransactionKind kind, bool isRecurring)
    {
        RunInSta(() =>
        {
            var vm = CreateVm(kind, CreateCheckingSource(balance: 500m), isRecurring: isRecurring, amount: 0m);

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.False(result.IsSuccess);
            Assert.Equal("Please enter a valid amount greater than zero.", result.ErrorMessage);
        });
    }

    [Fact]
    public void SelectedSource_IsRequired()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Income, CreateCheckingSource(balance: 500m), isRecurring: false, amount: 10m);
            vm.SelectedAccount = null;

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.False(result.IsSuccess);
            Assert.Equal("Please choose a account.", result.ErrorMessage);
        });
    }

    [Fact]
    public void SelectedTag_IsRequired_ForExpense()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Expense, CreateCheckingSource(balance: 500m), isRecurring: false, amount: 10m);
            vm.SelectedTag = null;

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.False(result.IsSuccess);
            Assert.Equal("Please choose a tag.", result.ErrorMessage);
        });
    }

    [Fact]
    public void SelectedGoal_IsRequired_ForGoalUpdate()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Goal, CreateCheckingSource(balance: 500m), isRecurring: false, amount: 10m);
            vm.SelectedGoal = null;

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.False(result.IsSuccess);
            Assert.Equal("Please choose a goal.", result.ErrorMessage);
        });
    }

    [Theory]
    [InlineData(TransactionKind.Expense, false)]
    [InlineData(TransactionKind.Expense, true)]
    [InlineData(TransactionKind.Goal, false)]
    [InlineData(TransactionKind.Goal, true)]
    public void Spending_OverflowOverMaximumSpending_IsInvalid(TransactionKind kind, bool isRecurring)
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m, maximumSpending: 100m, moneyOut: 95m);
            var vm = CreateVm(kind, source, isRecurring: isRecurring, amount: 10m);

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.False(result.IsSuccess);
            Assert.Equal("Amount exceeds this source's maximum spending limit.", result.ErrorMessage);
        });
    }

    [Theory]
    [InlineData(TransactionKind.Expense)]
    [InlineData(TransactionKind.Goal)]
    public void Spending_OverflowOverBalance_IsInvalid(TransactionKind kind)
    {
        RunInSta(() =>
        {
            var vm = CreateVm(kind, CreateCheckingSource(balance: 20m), isRecurring: false, amount: 30m);

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.False(result.IsSuccess);
            Assert.Equal("Amount exceeds this source's available balance.", result.ErrorMessage);
        });
    }

    [Theory]
    [InlineData(TransactionKind.Expense)]
    public void Spending_OverflowOverAccountLimit_IsInvalid(TransactionKind kind)
    {
        RunInSta(() =>
        {
            var source = CreateCreditSource(accountLimit: 100m, spentAmount: 95m);
            var vm = CreateVm(kind, source, isRecurring: false, amount: 10m);

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.False(result.IsSuccess);
            Assert.Equal("Amount exceeds this source's account limit.", result.ErrorMessage);
        });
    }

    [Fact]
    public void Income_IsExempt_FromSpendingOverflowChecks()
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 1m, maximumSpending: 1m, moneyOut: 1m);
            var vm = CreateVm(TransactionKind.Income, source, isRecurring: false, amount: 999m);

            Assert.False(vm.HasErrors);
            Assert.True(vm.CanSave);
        });
    }

    [Fact]
    public void RecurringTime_UsesSameValidationFlow()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(
                TransactionKind.Expense,
                CreateCheckingSource(balance: 500m),
                isRecurring: true,
                amount: 10m,
                recurringPeriod: RecurringPeriod.Weekly,
                recurringTimeText: "8");

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.False(result.IsSuccess);
            Assert.Equal("Recurring weekday must be between Monday and Sunday.", result.ErrorMessage);
        });
    }

    [Fact]
    public void SaveAsync_KeepsStaleSpendingCapacityCheck()
    {
        RunInSta(() =>
        {
            var sourceVm = CreateCheckingSource(balance: 200m);
            var staleSource = new Account
            {
                Id = sourceVm.Id,
                Name = sourceVm.Name,
                AccountType = sourceVm.AccountType,
                Balance = 10m,
                AccountLimit = 0m,
                MaximumSpending = 0m,
                SpentAmount = 0m,
                IsEnabled = true
            };

            var appData = CreateAppData();
            appData.GetAccountByIdAsync(sourceVm.Id, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<Account?>(staleSource));

            var vm = CreateVm(TransactionKind.Expense, sourceVm, isRecurring: false, amount: 25m, appData: appData);
            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.False(result.IsSuccess);
            Assert.Equal("Amount exceeds this source's available balance.", result.ErrorMessage);
        });
    }

    [Fact]
    public void SaveAsync_ResetAfterSave_RestoresAllNonSystemTags()
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m);
            var appData = CreateAppData();
            appData.GetTagsAsync(Arg.Any<CancellationToken>()).Returns(
            [
                new Tag { Id = 1, Name = "zeta", HexCode = "#111111" },
                new Tag { Id = 2, Name = "Alpha", HexCode = "#222222" },
                new Tag { Id = 3, Name = "System", HexCode = "#333333", IsSystemTag = true }
            ]);
            var vm = new TransactionPopupVM(CreateMainViewModel([source]), appData);
            vm.EnsureTagsLoadedAsync().GetAwaiter().GetResult();
            vm.SelectedTag = vm.VisibleTags.Concat(vm.OverflowTags).Single(tag => tag.Id == 1);
            vm.NameText = "Groceries";
            vm.AmountText = 10m;

            var result = vm.SaveAsync(resetAfterSave: true).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal([2, 1], vm.VisibleTags.Concat(vm.OverflowTags).Select(tag => tag.Id));
            Assert.Equal(2, vm.SelectedTag?.Id);
        });
    }

    [Fact]
    public void SaveAsync_KeepsStaleMaximumSpendingCheck_ForNonCreditSource()
    {
        RunInSta(() =>
        {
            var sourceVm = CreateCheckingSource(balance: 500m, maximumSpending: 100m, moneyOut: 0m);
            var staleSource = new Account
            {
                Id = sourceVm.Id,
                Name = sourceVm.Name,
                AccountType = sourceVm.AccountType,
                Balance = sourceVm.Balance,
                AccountLimit = 0m,
                MaximumSpending = 100m,
                SpentAmount = 95m,
                IsEnabled = true
            };

            var appData = CreateAppData();
            appData.GetAccountByIdAsync(sourceVm.Id, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<Account?>(staleSource));

            var vm = CreateVm(TransactionKind.Expense, sourceVm, isRecurring: false, amount: 10m, appData: appData);
            Assert.False(vm.HasErrors);
            Assert.True(vm.CanSave);

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.False(result.IsSuccess);
            Assert.Equal("Amount exceeds this source's maximum spending limit.", result.ErrorMessage);
        });
    }

    [Fact]
    public void HasSimilarTransactionAsync_FindsExpense_OnSelectedDate()
    {
        RunInSta(() =>
        {
            var selectedDate = new DateTime(2026, 6, 29);
            var existing = CreateTransaction("Valid name", 10.50m, sourceId: 1);
            existing.OccurredOn = selectedDate.AddHours(8);
            var appData = CreateAppData(expenseLogs: [existing]);
            var vm = CreateVm(
                TransactionKind.Expense,
                CreateCheckingSource(balance: 500m),
                isRecurring: false,
                amount: 10m,
                appData: appData);
            vm.SelectedDate = selectedDate;

            var result = vm.HasSimilarTransactionAsync().GetAwaiter().GetResult();

            Assert.True(result);
        });
    }

    [Fact]
    public void HasSimilarTransactionAsync_IgnoresExpense_OutsideSelectedDate()
    {
        RunInSta(() =>
        {
            var selectedDate = new DateTime(2026, 6, 29);
            var existing = CreateTransaction("Valid name", 10.50m, sourceId: 1);
            existing.OccurredOn = selectedDate.AddDays(-1).AddHours(23);
            var appData = CreateAppData(expenseLogs: [existing]);
            var vm = CreateVm(
                TransactionKind.Expense,
                CreateCheckingSource(balance: 500m),
                isRecurring: false,
                amount: 10m,
                appData: appData);
            vm.SelectedDate = selectedDate;

            var result = vm.HasSimilarTransactionAsync().GetAwaiter().GetResult();

            Assert.False(result);
        });
    }

    [Fact]
    public void HasSimilarTransactionAsync_IgnoresExpense_WhenAmountDiffersByMoreThanFivePercent()
    {
        RunInSta(() =>
        {
            var appData = CreateAppData(expenseLogs: [
                CreateTransaction("Valid name", 10.51m, sourceId: 1)
            ]);
            var vm = CreateVm(
                TransactionKind.Expense,
                CreateCheckingSource(balance: 500m),
                isRecurring: false,
                amount: 10m,
                appData: appData);

            var result = vm.HasSimilarTransactionAsync().GetAwaiter().GetResult();

            Assert.False(result);
        });
    }

    [Fact]
    public void HasSimilarTransactionAsync_IgnoresExpense_WhenSourceDiffers()
    {
        RunInSta(() =>
        {
            var appData = CreateAppData(expenseLogs: [
                CreateTransaction("Valid name", 10m, sourceId: 2)
            ]);
            var vm = CreateVm(
                TransactionKind.Expense,
                CreateCheckingSource(balance: 500m),
                isRecurring: false,
                amount: 10m,
                appData: appData);

            var result = vm.HasSimilarTransactionAsync().GetAwaiter().GetResult();

            Assert.False(result);
        });
    }

    [Fact]
    public void HasSimilarTransactionAsync_FindsIncome_WithSameNameTypeSourceAndNearAmount()
    {
        RunInSta(() =>
        {
            var appData = CreateAppData(incomeLogs: [
                new Transaction
                {
                    Type = TransactionType.Income,
                    Name = "Valid name",
                    Amount = 10.25m,
                    SourceAccountId = 1,
                    OccurredOn = DateTime.Today
                }
            ]);
            var vm = CreateVm(
                TransactionKind.Income,
                CreateCheckingSource(balance: 500m),
                isRecurring: false,
                amount: 10m,
                appData: appData);

            var result = vm.HasSimilarTransactionAsync().GetAwaiter().GetResult();

            Assert.True(result);
        });
    }

    [Fact]
    public void HasSimilarTransactionAsync_SeparatesGoalUpdatesFromExpenses()
    {
        RunInSta(() =>
        {
            var appData = CreateAppData(expenseLogs:
            [
                CreateTransaction("Goal Update: Goal", 10m, sourceId: 1, tagId: 1, tagName: "General")
            ]);
            var vm = CreateVm(
                TransactionKind.Goal,
                CreateCheckingSource(balance: 500m),
                isRecurring: false,
                amount: 10m,
                appData: appData);

            var result = vm.HasSimilarTransactionAsync().GetAwaiter().GetResult();

            Assert.False(result);
        });
    }

    [Fact]
    public void HasSimilarTransactionAsync_FindsGoalUpdate_WithSameGoalSourceAndNearAmount()
    {
        RunInSta(() =>
        {
            var appData = CreateAppData(expenseLogs:
            [
                CreateTransaction(
                    "Goal Update: Goal",
                    10.25m,
                    sourceId: 1,
                    tagId: 99,
                    tagName: GoalUpdateTransactionSupport.GoalUpdateTagName)
            ]);
            var vm = CreateVm(
                TransactionKind.Goal,
                CreateCheckingSource(balance: 500m),
                isRecurring: false,
                amount: 10m,
                appData: appData);

            var result = vm.HasSimilarTransactionAsync().GetAwaiter().GetResult();

            Assert.True(result);
        });
    }

    [Fact]
    public void Constructor_UsesAccountsOverride_WhenProvided()
    {
        RunInSta(() =>
        {
            var persistedSource = CreateCheckingSource(balance: 500m);
            var temporarySource = new AccountVM
            {
                Id = -11,
                Name = "Wizard Temporary",
                AccountType = AccountType.Checking,
                Balance = 250m,
                IsEnabled = true
            };

            var vm = new TransactionPopupVM(
                CreateMainViewModel([persistedSource]),
                CreateAppData(),
                accountsOverride: [temporarySource]);

            Assert.Single(vm.Accounts);
            Assert.Equal(-11, vm.Accounts.Single().Id);
            Assert.Equal("Wizard Temporary", vm.Accounts.Single().Name);
        });
    }

    [Fact]
    public void SaveAsync_RecurringDraftMode_UsesDraftCallback_ForTemporarySource()
    {
        RunInSta(() =>
        {
            var temporarySource = new AccountVM
            {
                Id = -7,
                Name = "Wizard Temporary",
                AccountType = AccountType.Checking,
                Balance = 500m,
                IsEnabled = true
            };

            var appData = CreateAppData();
            appData.GetAccountByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<Account?>(null));

            TransactionPopupVM.RecurringDraftSaveInput? captured = null;
            var vm = new TransactionPopupVM(
                CreateMainViewModel([CreateCheckingSource(balance: 100m)]),
                appData,
                accountsOverride: [temporarySource],
                saveRecurringDraftAsync: input =>
                {
                    captured = input;
                    return Task.FromResult(TransactionPopupVM.TransactionPopupSubmissionResult.Success());
                });

            vm.IsExpense = true;
            vm.IsRecurring = true;
            vm.NameText = "Rent";
            vm.AmountText = 50m;
            vm.SelectedRecurringPeriod = RecurringPeriod.Monthly;
            vm.RecurringTimeText = "10";
            vm.SelectedAccount = vm.Accounts.Single(source => source.Id == -7);

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess);
            Assert.True(captured.HasValue);
            Assert.Equal(RecurringTransactionType.Expense, captured.Value.Type);
            Assert.Equal(-7, captured.Value.AccountId);
            Assert.Equal(50m, captured.Value.Amount);
            Assert.Equal(ExpenseCategory.Needs, captured.Value.Category);
            appData.DidNotReceiveWithAnyArgs().GetAccountByIdAsync(default, default);
        });
    }

    [Fact]
    public void RecurringDraft_HardStopExhaustedCategory_StillSavesDraft()
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m);
            var allocation = new BudgetAllocation
            {
                AllocationLimit = 100m,
                AllocationPeriod = AllocationPeriod.Monthly,
                NeedsThreshold = 50,
                WantsThreshold = 30,
                InvestThreshold = 20,
                OverspendPolicy = OverspendPolicy.HardStop
            };
            var appData = CreateAppData(allocation, CreateTransactionsForBudget(ExpenseCategory.Wants, 30m));
            TransactionPopupVM.RecurringDraftSaveInput? captured = null;
            var vm = new TransactionPopupVM(
                CreateMainViewModel([source]),
                appData,
                saveRecurringDraftAsync: input =>
                {
                    captured = input;
                    return Task.FromResult(TransactionPopupVM.TransactionPopupSubmissionResult.Success());
                });
            vm.IsExpense = true;
            vm.IsRecurring = true;
            vm.NameText = "Subscription";
            vm.AmountText = 5m;
            vm.SelectedExpenseCategory = ExpenseCategory.Wants;
            vm.SelectedRecurringPeriod = RecurringPeriod.Monthly;
            vm.RecurringTimeText = "10";

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess);
            Assert.True(captured.HasValue);
            Assert.Equal(RecurringTransactionType.Expense, captured.Value.Type);
            Assert.Equal(ExpenseCategory.Wants, captured.Value.Category);
            appData.DidNotReceive().UpdateBudgetAllocation(Arg.Any<BudgetAllocation>());
        });
    }

    [Fact]
    public void RecurringDraft_SoftDebt_DoesNotAddBudgetDebt()
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m);
            var allocation = new BudgetAllocation
            {
                AllocationLimit = 100m,
                AllocationPeriod = AllocationPeriod.Monthly,
                NeedsThreshold = 50,
                WantsThreshold = 30,
                InvestThreshold = 20,
                OverspendPolicy = OverspendPolicy.SoftDebt
            };
            var appData = CreateAppData(allocation, CreateTransactionsForBudget(ExpenseCategory.Wants, 30m));
            var vm = new TransactionPopupVM(
                CreateMainViewModel([source]),
                appData,
                saveRecurringDraftAsync: _ => Task.FromResult(TransactionPopupVM.TransactionPopupSubmissionResult.Success()));
            vm.IsExpense = true;
            vm.IsRecurring = true;
            vm.NameText = "Subscription";
            vm.AmountText = 5m;
            vm.SelectedExpenseCategory = ExpenseCategory.Wants;
            vm.SelectedRecurringPeriod = RecurringPeriod.Monthly;
            vm.RecurringTimeText = "10";

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess);
            Assert.Equal(0m, allocation.WantsDebt);
            appData.DidNotReceive().UpdateBudgetAllocation(Arg.Any<BudgetAllocation>());
        });
    }

    [Fact]
    public void Expense_HardStop_BlocksOverspendingCategory()
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m);
            var allocation = new BudgetAllocation
            {
                AllocationLimit = 100m,
                AllocationPeriod = AllocationPeriod.Monthly,
                NeedsThreshold = 50,
                WantsThreshold = 30,
                InvestThreshold = 20,
                OverspendPolicy = OverspendPolicy.HardStop
            };
            var appData = CreateAppData(allocation, CreateTransactionsForBudget(ExpenseCategory.Wants, 25m));
            var vm = CreateVm(TransactionKind.Expense, source, isRecurring: false, amount: 6m, appData: appData);
            vm.SelectedExpenseCategory = ExpenseCategory.Wants;

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.False(result.IsSuccess);
            Assert.Equal("Wants budget is exhausted for this allocation period.", result.ErrorMessage);
            appData.DidNotReceiveWithAnyArgs().AddTransactionAsync(default!, default);
        });
    }

    [Fact]
    public void ExcludedExpense_HardStop_DoesNotBlockOrAddDebt()
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m);
            var allocation = new BudgetAllocation
            {
                AllocationLimit = 100m,
                AllocationPeriod = AllocationPeriod.Monthly,
                NeedsThreshold = 50,
                WantsThreshold = 30,
                InvestThreshold = 20,
                OverspendPolicy = OverspendPolicy.HardStop
            };
            var appData = CreateAppData(allocation, CreateTransactionsForBudget(ExpenseCategory.Wants, 30m));
            var vm = CreateVm(TransactionKind.Expense, source, isRecurring: false, amount: 10m, appData: appData);
            vm.SelectedExpenseCategory = ExpenseCategory.Wants;
            vm.IsExcludedFromBudget = true;

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(0m, allocation.WantsDebt);
        });
    }

    [Fact]
    public void Expense_HardStop_UsesExpenseDateAllocationPeriod()
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m);
            var allocation = new BudgetAllocation
            {
                AllocationLimit = 100m,
                AllocationPeriod = AllocationPeriod.Monthly,
                NeedsThreshold = 50,
                WantsThreshold = 30,
                InvestThreshold = 20,
                OverspendPolicy = OverspendPolicy.HardStop
            };
            var expenseDate = new DateTime(2026, 5, 15);
            var appData = CreateAppData(
                allocation,
                CreateTransactionsForBudget(ExpenseCategory.Wants, 29m, new DateTime(2026, 5, 1)));
            var vm = CreateVm(TransactionKind.Expense, source, isRecurring: false, amount: 2m, appData: appData);
            vm.SelectedExpenseCategory = ExpenseCategory.Wants;
            vm.SelectedDate = expenseDate;

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.False(result.IsSuccess);
            Assert.Equal("Wants budget is exhausted for this allocation period.", result.ErrorMessage);
        });
    }

    [Fact]
    public void Expense_SoftDebt_AddsCategoryDebt()
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m);
            var allocation = new BudgetAllocation
            {
                AllocationLimit = 100m,
                AllocationPeriod = AllocationPeriod.Monthly,
                NeedsThreshold = 50,
                WantsThreshold = 30,
                InvestThreshold = 20,
                OverspendPolicy = OverspendPolicy.SoftDebt
            };
            var appData = CreateAppData(allocation, CreateTransactionsForBudget(ExpenseCategory.Wants, 25m));
            var vm = CreateVm(TransactionKind.Expense, source, isRecurring: false, amount: 12m, appData: appData);
            vm.SelectedExpenseCategory = ExpenseCategory.Wants;

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess);
            Assert.Equal(7m, allocation.WantsDebt);
            appData.Received(1).UpdateBudgetAllocation(allocation);
        });
    }

    [Fact]
    public void GoalUpdate_HardStop_DoesNotBlockExcludedTransaction()
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m);
            var allocation = new BudgetAllocation
            {
                AllocationLimit = 100m,
                AllocationPeriod = AllocationPeriod.Monthly,
                NeedsThreshold = 50,
                WantsThreshold = 30,
                InvestThreshold = 20,
                OverspendPolicy = OverspendPolicy.HardStop
            };
            var appData = CreateAppData(allocation, CreateTransactionsForBudget(ExpenseCategory.Savings, 20m));
            var vm = CreateVm(TransactionKind.Goal, source, isRecurring: false, amount: 1m, appData: appData);

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess, result.ErrorMessage);
            appData.ReceivedWithAnyArgs().AddTransactionAsync(default!, default);
        });
    }

    [Fact]
    public void SaveAsync_GoalUpdate_IncrementsCurrentAmount()
    {
        RunInSta(() =>
        {
            var goal = new SavingGoal
            {
                Id = 1,
                Name = "Goal",
                TargetAmount = 500m,
                CurrentAmount = 100m
            };
            var appData = CreateAppData();
            appData.GetSavingGoalByIdAsync(1, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<SavingGoal?>(goal));
            var vm = CreateVm(
                TransactionKind.Goal,
                CreateCheckingSource(balance: 500m),
                isRecurring: false,
                amount: 25m,
                appData: appData);
            vm.SelectedGoal = new SavingGoalVM { Id = 1, Name = "Goal" };

            var result = vm.SaveAsync(false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess);
            Assert.Equal(125m, goal.CurrentAmount);
            appData.Received(1).AddTransactionAsync(
                Arg.Is<Transaction>(transaction => transaction.GoalId == goal.Id),
                Arg.Any<CancellationToken>());
            appData.Received(1).UpdateSavingGoal(goal);
        });
    }

    [Fact]
    public void SaveAsync_Expense_PersistsPinnedState()
    {
        RunInSta(() =>
        {
            var appData = CreateAppData();
            var vm = CreateVm(
                TransactionKind.Expense,
                CreateCheckingSource(balance: 500m),
                isRecurring: false,
                amount: 25m,
                appData: appData);
            vm.IsPinned = true;

            var result = vm.SaveAsync(false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess);
            appData.Received(1).AddTransactionAsync(
                Arg.Is<Transaction>(transaction => transaction.Type == TransactionType.Expense && transaction.IsPinned),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void SaveAsync_Expense_PersistsSelectedDateWithCurrentTime()
    {
        RunInSta(() =>
        {
            Transaction? savedTransaction = null;
            var appData = CreateAppData();
            appData.AddTransactionAsync(Arg.Do<Transaction>(transaction => savedTransaction = transaction), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            var vm = CreateVm(
                TransactionKind.Expense,
                CreateCheckingSource(balance: 500m),
                isRecurring: false,
                amount: 25m,
                appData: appData);
            vm.SelectedDate = new DateTime(2026, 6, 20);

            var result = vm.SaveAsync(false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess);
            Assert.NotNull(savedTransaction);
            Assert.Equal(new DateTime(2026, 6, 20), savedTransaction!.OccurredOn.Date);
            Assert.NotEqual(TimeSpan.Zero, savedTransaction.OccurredOn.TimeOfDay);
        });
    }

    [Fact]
    public void SaveAsync_Expense_PersistsLendState()
    {
        RunInSta(() =>
        {
            var appData = CreateAppData();
            var vm = CreateVm(
                TransactionKind.Expense,
                CreateCheckingSource(balance: 500m),
                isRecurring: false,
                amount: 25m,
                appData: appData);
            vm.IsIoU = true;

            var result = vm.SaveAsync(false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess);
            appData.Received(1).AddTransactionAsync(
                Arg.Is<Transaction>(transaction => transaction.Type == TransactionType.Expense && transaction.IsIoU),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void SaveAsync_RecurringExpense_PersistsCategory()
    {
        RunInSta(() =>
        {
            var appData = CreateAppData();
            var vm = CreateVm(
                TransactionKind.Expense,
                CreateCheckingSource(balance: 500m),
                isRecurring: true,
                amount: 25m,
                appData: appData);
            vm.SelectedExpenseCategory = ExpenseCategory.Wants;

            var result = vm.SaveAsync(false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess);
            appData.Received(1).AddRecurringTransactionAsync(
                Arg.Is<RecurringTransaction>(transaction => transaction.Category == ExpenseCategory.Wants),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void SaveAsync_RecurringIncome_ClearsCategory()
    {
        RunInSta(() =>
        {
            var appData = CreateAppData();
            var vm = CreateVm(
                TransactionKind.Income,
                CreateCheckingSource(balance: 0m),
                isRecurring: true,
                amount: 25m,
                appData: appData);

            var result = vm.SaveAsync(false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess);
            appData.Received(1).AddRecurringTransactionAsync(
                Arg.Is<RecurringTransaction>(transaction => transaction.Category == null),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void HandleRecurringModeClick_SelectsRecurring()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Expense, CreateCheckingSource(balance: 500m), isRecurring: false);
            vm.IsIoU = true;

            vm.HandleRecurringModeClick();

            Assert.True(vm.IsRecurring);
            Assert.False(vm.IsInstallments);
            Assert.False(vm.IsIoU);
            Assert.False(vm.CanPinTransaction);
        });
    }

    [Fact]
    public void HandleInstallmentsModeClick_SelectsInstallments()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Income, CreateCheckingSource(balance: 0m), isRecurring: true);

            vm.HandleInstallmentsModeClick();

            Assert.False(vm.IsRecurring);
            Assert.True(vm.IsInstallments);
            Assert.False(vm.IsIoU);
            Assert.True(vm.ShowInstallmentEndDate);
        });
    }

    [Fact]
    public void HandleIoUModeClick_SelectsIoU()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Expense, CreateCheckingSource(balance: 500m), isRecurring: false);
            vm.IsInstallments = true;

            vm.HandleIoUModeClick();

            Assert.False(vm.IsRecurring);
            Assert.False(vm.IsInstallments);
            Assert.True(vm.IsIoU);
        });
    }

    [Fact]
    public void IoUModes_SelectPostedStateAndDescription()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Expense, CreateCheckingSource(balance: 500m), false);

            vm.IsUnpostedIoUMode = true;
            Assert.True(vm.IsIoU);
            Assert.False(vm.ShouldAffectBalance);
            Assert.Equal("A transaction marked as debt/IoU but doesn't affect the accounts", vm.TransactionModeDescription);

            vm.IsPostedIoUMode = true;
            Assert.True(vm.IsIoU);
            Assert.True(vm.ShouldAffectBalance);
            Assert.Equal("A transaction marked as debt/IoU and affects the accounts", vm.TransactionModeDescription);
        });
    }

    [Theory]
    [InlineData(false, 500)]
    [InlineData(true, 475)]
    public void SaveAsync_IoUBalanceImpactMatchesPostedMode(bool posted, decimal expectedBalance)
    {
        RunInSta(() =>
        {
            var account = new Account
            {
                Id = 1,
                Name = "Checking",
                AccountType = AccountType.Checking,
                Balance = 500m,
                IsEnabled = true
            };
            var appData = CreateAppData();
            appData.GetAccountByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
            var vm = CreateVm(
                TransactionKind.Expense,
                CreateCheckingSource(balance: account.Balance),
                false,
                25m,
                appData: appData);
            vm.IsIoU = true;
            vm.ShouldAffectBalance = posted;

            var result = vm.SaveAsync(false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(expectedBalance, account.Balance);
            _ = appData.Received(1).AddTransactionAsync(
                Arg.Is<Transaction>(transaction =>
                    transaction.IsIoU && transaction.ShouldAffectBalance == posted),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void HandleExcludeModeClick_SelectsExclusionOnly()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Expense, CreateCheckingSource(balance: 500m), isRecurring: true);
            vm.IsIoU = true;

            vm.HandleExcludeModeClick();

            Assert.False(vm.IsRecurring);
            Assert.False(vm.IsInstallments);
            Assert.False(vm.IsIoU);
            Assert.True(vm.IsExcludedFromBudget);
        });
    }

    [Fact]
    public void HandleExcludedIoUModeClick_SelectsIoUAndExclusion()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Expense, CreateCheckingSource(balance: 500m), isRecurring: true);

            vm.HandleExcludedIoUModeClick();

            Assert.False(vm.IsRecurring);
            Assert.False(vm.IsInstallments);
            Assert.True(vm.IsIoU);
            Assert.True(vm.IsExcludedFromBudget);
        });
    }

    [Fact]
    public void HandleInstallmentsModeClick_ForGoalUpdate_KeepsRecurringSelected()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Goal, CreateCheckingSource(balance: 500m), isRecurring: true);

            vm.HandleInstallmentsModeClick();

            Assert.True(vm.IsRecurring);
            Assert.False(vm.IsInstallments);
        });
    }

    [Fact]
    public void SwitchingToGoalUpdate_ClearsInstallments()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Expense, CreateCheckingSource(balance: 500m), isRecurring: true);
            vm.IsInstallments = true;

            vm.IsGoal = true;

            Assert.False(vm.IsInstallments);
            Assert.False(vm.CanUseInstallments);
            Assert.False(vm.ShowInstallmentEndDate);
        });
    }

    [Theory]
    [InlineData(TransactionKind.Expense, "paid")]
    [InlineData(TransactionKind.Income, "earned")]
    public void InstallmentSummaryText_UsesSplitAmountRecurrenceLabelAndKindVerb(
        TransactionKind kind,
        string expectedVerb)
    {
        RunInSta(() =>
        {
            var vm = CreateVm(kind, CreateCheckingSource(balance: 500m), isRecurring: true, amount: 21.5m);
            vm.StartDate = new DateTime(2026, 6, 20);
            vm.SelectedRecurringPeriod = RecurringPeriod.Monthly;
            vm.RecurringTimeText = "1";
            vm.InstallmentEndDate = new DateTime(2026, 10, 10);
            vm.IsInstallments = true;

            Assert.Equal($"The installment will be 5.38, {expectedVerb} every 1st", vm.InstallmentSummaryText);
        });
    }

    [Fact]
    public void Installments_UseClosestMatchingStartDate_WhenNextOccurrenceIsClosest()
    {
        RunInSta(() =>
        {
            var appData = CreateAppData();
            var vm = CreateVm(
                TransactionKind.Expense,
                CreateCheckingSource(balance: 500m),
                isRecurring: true,
                amount: 90m,
                name: "Laptop",
                appData: appData);
            vm.StartDate = new DateTime(2026, 6, 18);
            vm.SelectedRecurringPeriod = RecurringPeriod.Monthly;
            vm.RecurringTimeText = "20";
            vm.InstallmentEndDate = new DateTime(2026, 8, 21);
            vm.IsInstallments = true;

            var result = vm.SaveAsync(false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess);
            appData.Received(1).AddRecurringTransactionAsync(
                Arg.Is<RecurringTransaction>(transaction => transaction.Amount == 30m),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void CanSave_Installments_ValidatesSplitAmountAgainstSourceCapacity()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Expense, CreateCheckingSource(balance: 30m), isRecurring: true, amount: 100m);
            vm.StartDate = new DateTime(2026, 6, 20);
            vm.SelectedRecurringPeriod = RecurringPeriod.Monthly;
            vm.RecurringTimeText = "1";
            vm.InstallmentEndDate = new DateTime(2026, 10, 10);
            vm.IsInstallments = true;

            Assert.True(vm.CanSave);
        });
    }

    [Fact]
    public void CanSave_Installments_ValidatesSplitAmountAgainstMaximumSpending()
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m, maximumSpending: 100m, moneyOut: 70m);
            var vm = CreateVm(TransactionKind.Expense, source, isRecurring: true, amount: 100m);
            vm.StartDate = new DateTime(2026, 6, 20);
            vm.SelectedRecurringPeriod = RecurringPeriod.Monthly;
            vm.RecurringTimeText = "1";
            vm.InstallmentEndDate = new DateTime(2026, 10, 10);
            vm.IsInstallments = true;
            vm.ValidateAmountField();

            Assert.True(vm.CanSave);
            Assert.Empty(vm.GetErrors(nameof(TransactionPopupVM.AmountText)));
            Assert.Equal(string.Empty, vm.AmountValidationHint);
        });
    }

    [Fact]
    public void SaveAsync_Installments_ValidatesSplitAmountAgainstPersistedMaximumSpending()
    {
        RunInSta(() =>
        {
            var sourceVm = CreateCheckingSource(balance: 500m, maximumSpending: 100m, moneyOut: 0m);
            var staleSource = new Account
            {
                Id = sourceVm.Id,
                Name = sourceVm.Name,
                AccountType = sourceVm.AccountType,
                Balance = sourceVm.Balance,
                AccountLimit = 0m,
                MaximumSpending = 100m,
                SpentAmount = 70m,
                IsEnabled = true
            };

            var appData = CreateAppData();
            appData.GetAccountByIdAsync(sourceVm.Id, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<Account?>(staleSource));

            var vm = CreateVm(TransactionKind.Expense, sourceVm, isRecurring: true, amount: 100m, appData: appData);
            vm.StartDate = new DateTime(2026, 6, 20);
            vm.SelectedRecurringPeriod = RecurringPeriod.Monthly;
            vm.RecurringTimeText = "1";
            vm.InstallmentEndDate = new DateTime(2026, 10, 10);
            vm.IsInstallments = true;

            var result = vm.SaveAsync(resetAfterSave: false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess);
        });
    }

    [Fact]
    public void AmountValidation_Installments_DoesNotValidateTotalWhenRecurrenceCountIsPending()
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m, maximumSpending: 100m, moneyOut: 70m);
            var vm = CreateVm(TransactionKind.Expense, source, isRecurring: true, amount: 100m);
            vm.StartDate = new DateTime(2026, 6, 20);
            vm.SelectedRecurringPeriod = RecurringPeriod.Monthly;
            vm.RecurringTimeText = "1";
            vm.InstallmentEndDate = new DateTime(2026, 6, 20);
            vm.IsInstallments = true;
            vm.ValidateAmountField();

            Assert.Empty(vm.GetErrors(nameof(TransactionPopupVM.AmountText)));
            Assert.Equal(string.Empty, vm.AmountValidationHint);
            Assert.False(vm.CanSave);
        });
    }

    [Fact]
    public void AmountValidation_WhenActive_RevalidatesAfterSwitchingToInstallments()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Expense, CreateCheckingSource(balance: 30m), isRecurring: true, amount: 100m);
            vm.StartDate = new DateTime(2026, 6, 20);
            vm.SelectedRecurringPeriod = RecurringPeriod.Monthly;
            vm.RecurringTimeText = "1";
            vm.InstallmentEndDate = new DateTime(2026, 10, 10);
            vm.ValidateAmountField();

            Assert.Contains(vm.GetErrors(nameof(TransactionPopupVM.AmountText)),
                error => error.ErrorMessage == "Amount exceeds this source's available balance.");

            vm.IsInstallments = true;

            Assert.Empty(vm.GetErrors(nameof(TransactionPopupVM.AmountText)));
            Assert.Equal(string.Empty, vm.AmountValidationHint);
        });
    }

    [Fact]
    public void CanSave_Installments_ValidatesSplitAmountUsingClosestMatchingStartDate()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Expense, CreateCheckingSource(balance: 25m), isRecurring: true, amount: 90m);
            vm.StartDate = new DateTime(2026, 6, 18);
            vm.SelectedRecurringPeriod = RecurringPeriod.Monthly;
            vm.RecurringTimeText = "20";
            vm.InstallmentEndDate = new DateTime(2026, 8, 21);
            vm.IsInstallments = true;
            vm.ValidateAmountField();

            Assert.False(vm.CanSave);
            Assert.Contains(vm.GetErrors(nameof(TransactionPopupVM.AmountText)),
                error => error.ErrorMessage == "Amount exceeds this source's available balance.");
            Assert.Equal("Insufficient Balance", vm.AmountValidationHint);
        });
    }

    [Theory]
    [InlineData(TransactionKind.Expense, RecurringTransactionType.Expense)]
    [InlineData(TransactionKind.Income, RecurringTransactionType.Income)]
    public void SaveAsync_Installments_CreatesRecurringWithInstallmentNameAndSplitAmount(
        TransactionKind kind,
        RecurringTransactionType expectedType)
    {
        RunInSta(() =>
        {
            var appData = CreateAppData();
            var vm = CreateVm(
                kind,
                CreateCheckingSource(balance: 500m),
                isRecurring: true,
                amount: 100m,
                name: "Laptop",
                appData: appData);
            vm.StartDate = new DateTime(2026, 6, 20);
            vm.SelectedRecurringPeriod = RecurringPeriod.Monthly;
            vm.RecurringTimeText = "1";
            vm.InstallmentEndDate = new DateTime(2026, 10, 10);
            vm.IsInstallments = true;

            var result = vm.SaveAsync(false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess);
            appData.Received(1).AddRecurringTransactionAsync(
                Arg.Is<RecurringTransaction>(transaction =>
                    transaction.Type == expectedType
                    && transaction.Name == "Installments for Laptop"
                    && transaction.Amount == 25m),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void SaveAsync_Income_PersistsPinnedState()
    {
        RunInSta(() =>
        {
            var appData = CreateAppData();
            var vm = CreateVm(
                TransactionKind.Income,
                CreateCheckingSource(balance: 0m),
                isRecurring: false,
                amount: 25m,
                appData: appData);
            vm.IsPinned = true;

            var result = vm.SaveAsync(false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess);
            appData.Received(1).AddTransactionAsync(
                Arg.Is<Transaction>(transaction => transaction.Type == TransactionType.Income && transaction.IsPinned),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void SaveAsync_Income_PersistsSelectedDateWithCurrentTime()
    {
        RunInSta(() =>
        {
            Transaction? savedTransaction = null;
            var appData = CreateAppData();
            appData.AddTransactionAsync(Arg.Do<Transaction>(transaction => savedTransaction = transaction), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            var vm = CreateVm(
                TransactionKind.Income,
                CreateCheckingSource(balance: 0m),
                isRecurring: false,
                amount: 25m,
                appData: appData);
            vm.SelectedDate = new DateTime(2026, 6, 20);

            var result = vm.SaveAsync(false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess);
            Assert.NotNull(savedTransaction);
            Assert.Equal(new DateTime(2026, 6, 20), savedTransaction!.OccurredOn.Date);
            Assert.NotEqual(TimeSpan.Zero, savedTransaction.OccurredOn.TimeOfDay);
        });
    }

    [Fact]
    public void SaveAsync_Income_PersistsDebtState()
    {
        RunInSta(() =>
        {
            var appData = CreateAppData();
            var vm = CreateVm(
                TransactionKind.Income,
                CreateCheckingSource(balance: 0m),
                isRecurring: false,
                amount: 25m,
                appData: appData);
            vm.IsIoU = true;

            var result = vm.SaveAsync(false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess);
            appData.Received(1).AddTransactionAsync(
                Arg.Is<Transaction>(transaction => transaction.Type == TransactionType.Income && transaction.IsIoU),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void GoalUpdate_KeepsHistoryAvailableAndOpen()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Expense, CreateCheckingSource(balance: 500m), isRecurring: false);
            vm.IsHistoryOpen = true;

            vm.IsGoal = true;

            Assert.True(vm.CanUseHistory);
            Assert.True(vm.IsHistoryOpen);
        });
    }

    [Fact]
    public void LoadHistoryAsync_ForGoalUpdateKeepsPinnedEmptyAndLoadsSameGoalHistory()
    {
        RunInSta(() =>
        {
            var appData = CreateAppData(expenseLogs:
            [
                CreateTransaction("Goal Update: Goal", 25m, sourceId: 1, tagId: 99, tagName: "Goal Update"),
                CreateTransaction("Goal Update: Other", 30m, sourceId: 1, tagId: 99, tagName: "Goal Update")
            ]);
            var vm = CreateVm(
                TransactionKind.Goal,
                CreateCheckingSource(balance: 500m),
                isRecurring: false,
                appData: appData);

            vm.LoadHistoryAsync().GetAwaiter().GetResult();

            Assert.Empty(vm.PinnedHistory.Items);
            var item = Assert.Single(vm.TransactionHistory.Items);
            Assert.Equal("Goal Update: Goal", item.Name);
        });
    }

    [Fact]
    public void SelectingHistoryItem_FillsFieldsAndKeepsRecurringToggle()
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m);
            var vm = CreateVm(TransactionKind.Expense, source, isRecurring: true);
            var item = new AddNewTransactionHistoryItemVM
            {
                Id = 10,
                IsExpense = true,
                Name = "Coffee",
                Amount = 4.5m,
                AccountId = source.Id,
                AccountName = source.Name,
                Note = "morning",
                Date = new DateTime(2026, 6, 1),
                Category = ExpenseCategory.Wants,
                TagId = vm.SelectedTag?.Id,
                IsPinned = true
            };

            vm.SelectedHistoryItem = item;

            Assert.Equal("Coffee", vm.NameText);
            Assert.Equal(4.5m, vm.AmountText);
            Assert.Equal("morning", vm.NoteText);
            Assert.Equal(new DateTime(2026, 6, 1), vm.SelectedDate);
            Assert.True(vm.IsPinned);
            Assert.True(vm.IsRecurring);
            Assert.Same(source, vm.SelectedAccount);
        });
    }

    [Fact]
    public void ModeBindings_DefaultToRegular_AndGoalForcesBudgetExclusion()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Expense, CreateCheckingSource(balance: 500m), isRecurring: false);
            Assert.True(vm.IsRegularMode);
            vm.IsGoal = true;
            Assert.True(vm.IsBudgetExcluded);
            Assert.False(vm.CanToggleBudgetExclusion);
            Assert.False(vm.CanUseInstallments);
            Assert.False(vm.CanUseIoU);
            vm.IsExpense = true;
            Assert.False(vm.IsBudgetExcluded);
            Assert.True(vm.CanToggleBudgetExclusion);
        });
    }

    [Fact]
    public void HasChanges_TracksGeneratedGoalName()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Goal, CreateCheckingSource(balance: 500m), isRecurring: false);
            vm.BeginChangeTracking();
            vm.NameText = "Generated replacement";
            Assert.True(vm.HasChanges);
            vm.AmountText = 1m;
            Assert.True(vm.HasChanges);
        });
    }

    [Fact]
    public void ExpensePreview_ShowsCategoryAndAccountAmounts()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(
                TransactionKind.Expense,
                CreateCheckingSource(balance: 500m),
                isRecurring: false,
                amount: 25m,
                appData: CreateAppData(expenseLogs: CreateTransactionsForBudget(ExpenseCategory.Needs, 40m)));

            Assert.True(vm.ShowCategoryImpact);
            Assert.True(vm.ShowAccountImpact);
            Assert.Equal(40m, vm.CategoryCurrent);
            Assert.Equal(65m, vm.CategoryToBe);
            Assert.Equal(500m, vm.AccountCurrent);
            Assert.Equal(475m, vm.AccountToBe);
        });
    }

    [Fact]
    public void IncomePreview_HidesCategoryAndIncreasesAccount()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Income, CreateCheckingSource(balance: 500m), isRecurring: false, amount: 25m);

            Assert.False(vm.ShowCategoryImpact);
            Assert.True(vm.ShowAccountImpact);
            Assert.Equal(500m, vm.AccountCurrent);
            Assert.Equal(525m, vm.AccountToBe);
        });
    }

    [Fact]
    public void UnpostedIoUPreview_HidesBothImpacts()
    {
        RunInSta(() =>
        {
            var vm = CreateVm(TransactionKind.Expense, CreateCheckingSource(balance: 500m), isRecurring: false, amount: 25m);
            vm.IsUnpostedIoUMode = true;

            Assert.False(vm.ShowCategoryImpact);
            Assert.False(vm.ShowAccountImpact);
        });
    }

    private static TransactionPopupVM CreateVm(
        TransactionKind kind,
        AccountVM source,
        bool isRecurring,
        decimal amount = 10m,
        string name = "Valid name",
        RecurringPeriod recurringPeriod = RecurringPeriod.Monthly,
        string recurringTimeText = "1",
        IAppDataService? appData = null)
    {
        var main = CreateMainViewModel([source]);
        var data = appData ?? CreateAppData();
        var vm = new TransactionPopupVM(main, data);

        switch (kind)
        {
            case TransactionKind.Expense:
                vm.IsExpense = true;
                vm.IsGoal = false;
                break;
            case TransactionKind.Income:
                vm.IsIncome = true;
                break;
            case TransactionKind.Goal:
                vm.IsGoal = true;
                break;
        }

        vm.AmountText = amount;
        vm.NameText = name;
        vm.IsRecurring = isRecurring;
        vm.SelectedRecurringPeriod = recurringPeriod;
        vm.RecurringTimeText = recurringTimeText;
        return vm;
    }

    private static IAppDataService CreateAppData(
        BudgetAllocation? budgetAllocation = null,
        IReadOnlyList<Transaction>? expenseLogs = null,
        IReadOnlyList<Transaction>? incomeLogs = null)
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Transaction>>(
                (expenseLogs ?? []).Concat(incomeLogs ?? []).ToList()));
        appData.GetAccountByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<Account?>(new Account
            {
                Id = callInfo.ArgAt<int>(0),
                Name = "Default",
                AccountType = AccountType.Checking,
                Balance = 1_000m,
                AccountLimit = 0m,
                MaximumSpending = 0m,
                SpentAmount = 0m,
                IsEnabled = true
            }));
        appData.GetTagByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<Tag?>(new Tag
            {
                Id = callInfo.ArgAt<int>(0),
                Name = "General",
                HexCode = "#22C55E",
                IsSystemTag = false
            }));
        appData.GetSavingGoalByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<SavingGoal?>(new SavingGoal
            {
                Id = callInfo.ArgAt<int>(0),
                Name = "Goal",
                TargetAmount = 500m,
                CurrentAmount = 100m
            }));
        appData.GetTagsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Tag>>(
            [
                new Tag
                {
                    Id = 99,
                    Name = GoalUpdateTransactionSupport.GoalUpdateTagName,
                    HexCode = GoalUpdateTransactionSupport.GoalUpdateTagColor,
                    IsSystemTag = false
                }
            ]));
        appData.AddTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        appData.AddTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        appData.AddTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        appData.AddRecurringTransactionAsync(Arg.Any<RecurringTransaction>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        appData.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        appData.GetBudgetAllocationAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(budgetAllocation ?? new BudgetAllocation()));
        return appData;
    }

    private static TransactionPopupVM CreateRepaymentVm(decimal spentAmount)
    {
        var checking = CreateCheckingSource(balance: 500m);
        var credit = new AccountVM
        {
            Id = 2,
            Name = "Visa",
            AccountType = AccountType.Credit,
            IsEnabled = true,
            SpentAmount = spentAmount,
            DeductSource = checking.Id
        };
        var vm = new TransactionPopupVM(
            CreateMainViewModel([checking, credit]),
            CreateAppData());
        vm.InitializeRepayment(credit);
        return vm;
    }

    private static MainVM CreateMainViewModel(IReadOnlyList<AccountVM> accounts)
    {
        var messenger = new WeakReferenceMessenger();
        var mapper = Substitute.For<IMapper>();
        var unitOfWork = CreateUnitOfWork();
        var dataOperationRunner = new InlineDataOperationRunner(unitOfWork);
        mapper.Map<IReadOnlyList<TransactionVM>>(Arg.Any<object>()).Returns([]);
        mapper.Map<IReadOnlyList<AccountVM>>(Arg.Any<object>()).Returns([]);
        mapper.Map<IReadOnlyList<TagVM>>(Arg.Any<object>()).Returns([]);
        mapper.Map<IReadOnlyList<Fluxo.Core.DTO.RecurringTransactionDto>>(Arg.Any<object>()).Returns([]);
        mapper.Map<IReadOnlyList<RecurringTransactionVM>>(Arg.Any<object>()).Returns([]);
        mapper.Map<IReadOnlyList<Fluxo.Core.DTO.SavingGoalDto>>(Arg.Any<object>()).Returns([]);
        mapper.Map<IReadOnlyList<SavingGoalVM>>(Arg.Any<object>()).Returns([]);

        var transactionService = Substitute.For<ITransactionService>();
        transactionService.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Fluxo.Core.DTO.TransactionDto>>([]));
        var accountService = Substitute.For<IAccountService>();
        accountService.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Fluxo.Core.DTO.AccountDto>>([]));
        var tagService = Substitute.For<ITagService>();
        tagService.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Fluxo.Core.DTO.TagDto>>([]));

        var dashboard = new DashboardVM(
            new NotificationPanelVM(
                transactionService,
                accountService,
                dataOperationRunner,
                mapper,
                messenger: messenger),
            new BudgetAllocationPanelVM(
                transactionService,
                accountService,
                tagService,
                dataOperationRunner,
                mapper,
                messenger),
            new SpentAllowancePanelVM(
                transactionService,
                accountService,
                dataOperationRunner,
                mapper,
                messenger),
            new SavingGoalsPanelVM(dataOperationRunner, mapper, messenger),
            new UpcomingEventsPanelVM(dataOperationRunner, mapper, messenger: messenger),
            new MainViewModeToggleVM(messenger));
        var main = new MainVM(
            dataOperationRunner,
            dashboard,
            new DaySpinnerVM(messenger),
            null);

        foreach (var source in accounts)
            main.BudgetPanel.Accounts.Add(source);

        main.BudgetPanel.Tags = new ObservableCollection<TagVM>(
        [
            new TagVM
            {
                Id = 1,
                Name = "General",
                HexCode = "#22C55E",
                IsSystemTag = false
            }
        ]);
        main.BudgetPanel.OtherTags = new ObservableCollection<TagVM>();
        main.SavingGoalsPanel.SavingGoals.Add(new SavingGoalVM
        {
            Id = 1,
            Name = "Goal",
            TargetAmount = 500m,
            CurrentAmount = 100m
        });

        return main;
    }

    private static IUnitOfWork CreateUnitOfWork()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var userSettings = Substitute.For<IUserSettingsRepository>();
        userSettings.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserSettings>>([]));
        unitOfWork.UserSettings.Returns(userSettings);

        var incomeLogs = Substitute.For<ITransactionRepository>();
        incomeLogs.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Transaction>>([]));
        unitOfWork.Transactions.Returns(incomeLogs);

        var budgetAllocation = Substitute.For<IBudgetAllocationRepository>();
        budgetAllocation.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BudgetAllocation?>(new BudgetAllocation()));
        unitOfWork.BudgetAllocation.Returns(budgetAllocation);

        return unitOfWork;
    }

    private static IReadOnlyList<Transaction> CreateTransactionsForBudget(
        ExpenseCategory category,
        decimal amount,
        DateTime? deductedOn = null)
    {
        return
        [
            new Transaction
            {
                Id = 10,
                Amount = amount,
                OccurredOn = deductedOn ?? DateTime.Today,
                IsForDeletion = false,
                Type = TransactionType.Expense,
                Name = "Existing",
                ExpenseCategory = category
            }
        ];
    }

    private static Transaction CreateTransaction(
        string name,
        decimal amount,
        int sourceId,
        int tagId = 1,
        string tagName = "General")
    {
        return new Transaction
        {
            Id = 10,
            Amount = amount,
            SourceAccountId = sourceId,
            OccurredOn = DateTime.Today,
            IsForDeletion = false,
            Type = TransactionType.Expense,
            Name = name,
            TagId = tagId,
            Tag = new Tag
            {
                Id = tagId,
                Name = tagName
            }
        };
    }

    private static AccountVM CreateCheckingSource(
        decimal balance,
        decimal maximumSpending = 0m,
        decimal moneyOut = 0m)
    {
        return new AccountVM
        {
            Id = 1,
            Name = "Checking",
            AccountType = AccountType.Checking,
            Balance = balance,
            MaximumSpending = maximumSpending,
            MoneyOut = moneyOut,
            IsEnabled = true
        };
    }

    private static AccountVM CreateCreditSource(
        decimal accountLimit,
        decimal spentAmount,
        decimal maximumSpending = 0m)
    {
        return new AccountVM
        {
            Id = 1,
            Name = "Credit",
            AccountType = AccountType.Credit,
            AccountLimit = accountLimit,
            SpentAmount = spentAmount,
            MaximumSpending = maximumSpending,
            IsEnabled = true
        };
    }

    [Fact]
    public void ProcessingNext_PersistsAndBackRestoresSavedEdits()
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m);
            var appData = CreateAppData();
            var vm = new TransactionPopupVM(CreateMainViewModel([source]), appData, [source]);
            var first = new RecurringTransactionVM
            {
                Id = 1, Name = "First", Amount = 10m, Type = RecurringTransactionType.Expense,
                Category = ExpenseCategory.Needs, Source = source, Tag = new TagVM { Id = 1, Name = "General" }
            };
            var second = new RecurringTransactionVM
            {
                Id = 2, Name = "Second", Amount = 20m, Type = RecurringTransactionType.Expense,
                Category = ExpenseCategory.Needs, Source = source, Tag = new TagVM { Id = 1, Name = "General" }
            };
            vm.InitializeRecurringProcessing([first, second]);

            Assert.Equal(PopupMode.BackNext, vm.PopupMode);
            Assert.Equal("Payment Processing", vm.PopupTitle);
            vm.NameText = "First edited";
            vm.AmountText = 12m;

            var result = vm.SaveCurrentAndAdvanceAsync().GetAwaiter().GetResult();
            vm.NameText = "Second edited";
            vm.NavigatePreviousProcessing();

            Assert.True(result.IsSuccess);
            Assert.Equal(first.Id, vm.CurrentProcessingRecurringTransactionId);
            Assert.Equal("First edited", vm.NameText);
            Assert.Equal(12m, vm.AmountText);
            appData.Received(1).AddTransactionAsync(Arg.Any<Transaction>());
        });
    }

    [Fact]
    public void ProcessingSkip_IsNotPersisted()
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m);
            var appData = CreateAppData();
            var vm = new TransactionPopupVM(CreateMainViewModel([source]), appData);
            var tag = new TagVM { Id = 1, Name = "General" };
            vm.InitializeRecurringProcessing(
            [
                new RecurringTransactionVM { Id = 1, Name = "First", Amount = 10m, Type = RecurringTransactionType.Expense, Category = ExpenseCategory.Needs, Source = source, Tag = tag },
                new RecurringTransactionVM { Id = 2, Name = "Second", Amount = 20m, Type = RecurringTransactionType.Expense, Category = ExpenseCategory.Needs, Source = source, Tag = tag }
            ]);

            Assert.True(vm.SaveCurrentAndAdvanceAsync().GetAwaiter().GetResult().IsSuccess);
            Assert.False(vm.SkipCurrentProcessing());
            Assert.True(vm.PersistProcessedItemsAsync().GetAwaiter().GetResult().IsSuccess);

            _ = appData.Received(1).AddTransactionAsync(Arg.Any<Transaction>());
        });
    }

    [Fact]
    public void InitializeView_UsesFunctionalReadOnlyMode()
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m);
            var vm = new TransactionPopupVM(CreateMainViewModel([source]), CreateAppData());

            vm.InitializeView(new TransactionVM
            {
                Id = 12,
                Type = TransactionType.Income,
                SourceAccountId = source.Id,
                Account = source,
                Name = "Salary",
                Amount = 100m,
                OccurredOn = new DateTime(2026, 7, 13)
            });

            Assert.True(vm.IsViewOnly);
            Assert.Equal(PopupMode.Functional, vm.PopupMode);
            Assert.False(vm.CanContinue);
            Assert.False(vm.CanDiscard);
            Assert.Equal("Transaction Detail", vm.PopupTitle);
            Assert.True(vm.CanEditViewedTransaction);
            Assert.True(vm.CanCloneViewedTransaction);
            Assert.True(vm.CanSplitViewedTransaction);
            Assert.Equal("Salary", vm.NameText);
        });
    }

    [Fact]
    public void SaveAsync_RecurringIncome_PersistsBudgetExclusion()
    {
        RunInSta(() =>
        {
            var appData = CreateAppData();
            var vm = CreateVm(
                TransactionKind.Income,
                CreateCheckingSource(balance: 0m),
                isRecurring: true,
                amount: 25m,
                appData: appData);

            var result = vm.SaveAsync(false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess, result.ErrorMessage);
            appData.Received(1).AddRecurringTransactionAsync(
                Arg.Is<RecurringTransaction>(transaction =>
                    transaction.Type == RecurringTransactionType.Income && transaction.IsExcludedFromBudget),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void SaveAsync_Income_PersistsBudgetExclusion()
    {
        RunInSta(() =>
        {
            var appData = CreateAppData();
            var vm = CreateVm(
                TransactionKind.Income,
                CreateCheckingSource(balance: 0m),
                isRecurring: false,
                amount: 25m,
                appData: appData);

            var result = vm.SaveAsync(false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess, result.ErrorMessage);
            appData.Received(1).AddTransactionAsync(
                Arg.Is<Transaction>(transaction =>
                    transaction.Type == TransactionType.Income && transaction.IsExcludedFromBudget),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void ViewedTransactionTags_SwitchBetweenSelectedOnlyAndEditableTagList()
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m);
            var tags = new[]
            {
                new Tag { Id = 1, Name = "Alpha", HexCode = "#111111" },
                new Tag { Id = 2, Name = "Bravo", HexCode = "#222222" },
                new Tag { Id = 3, Name = "Charlie", HexCode = "#333333" },
                new Tag { Id = 4, Name = "Delta", HexCode = "#444444" },
                new Tag { Id = 5, Name = "Echo", HexCode = "#555555" }
            };
            var appData = CreateAppData();
            appData.GetTagsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Tag>>(tags));
            var vm = new TransactionPopupVM(CreateMainViewModel([source]), appData);

            vm.InitializeView(new TransactionVM
            {
                Id = 15,
                Type = TransactionType.Expense,
                SourceAccountId = source.Id,
                Account = source,
                Tag = new TagVM { Id = 5, Name = "Echo", HexCode = "#555555" }
            });
            vm.EnsureTagsLoadedAsync().GetAwaiter().GetResult();

            Assert.Equal([5], vm.VisibleTags.Select(tag => tag.Id));
            Assert.Empty(vm.OverflowTags);

            vm.BeginEditingViewedTransactionAsync().GetAwaiter().GetResult();

            Assert.Equal([5, 1, 2, 3, 4], vm.VisibleTags.Concat(vm.OverflowTags).Select(tag => tag.Id));

            vm.DiscardEditingViewedTransaction();

            Assert.Equal([5], vm.VisibleTags.Select(tag => tag.Id));
            Assert.Empty(vm.OverflowTags);

            vm.BeginEditingViewedTransactionAsync().GetAwaiter().GetResult();

            Assert.Equal([5, 1, 2, 3, 4], vm.VisibleTags.Concat(vm.OverflowTags).Select(tag => tag.Id));
        });
    }

    [Fact]
    public void ViewMode_ClearsFeedbackAndRestoresTheSavedTagSelection()
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m);
            var vm = new TransactionPopupVM(CreateMainViewModel([source]), CreateAppData());
            var transaction = new TransactionVM
            {
                Id = 15,
                Type = TransactionType.Expense,
                SourceAccountId = source.Id,
                Account = source,
                Name = "Groceries",
                Amount = 25m,
                Tag = new TagVM { Id = 5, Name = "Food", HexCode = "#555555" }
            };

            vm.NameText = string.Empty;
            vm.AmountText = 0m;
            vm.ValidateNameField();
            vm.ValidateAmountField();

            vm.InitializeView(transaction);

            Assert.Equal(string.Empty, vm.NameValidationHint);
            Assert.Equal(string.Empty, vm.AmountValidationHint);
            Assert.Equal(string.Empty, vm.AmountWarningHint);
            Assert.Equal(string.Empty, vm.AmountFieldHint);

            vm.SelectedTag = null;

            Assert.Equal(transaction.Tag!.Id, vm.SelectedTag!.Id);
            Assert.Equal([transaction.Tag.Id], vm.VisibleTags.Select(tag => tag.Id));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnsureTagsLoadedAsync_AddAndEditModes_LoadAllTagsAndPromoteSelectedTag(bool editMode)
    {
        RunInSta(() =>
        {
            var source = CreateCheckingSource(balance: 500m);
            var tags = new[]
            {
                new Tag { Id = 1, Name = "Alpha", HexCode = "#111111" },
                new Tag { Id = 2, Name = "Bravo", HexCode = "#222222" },
                new Tag { Id = 3, Name = "Charlie", HexCode = "#333333" },
                new Tag { Id = 4, Name = "Delta", HexCode = "#444444" },
                new Tag { Id = 5, Name = "Echo", HexCode = "#555555" }
            };
            var appData = CreateAppData();
            appData.GetTagsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Tag>>(tags));
            var vm = new TransactionPopupVM(CreateMainViewModel([source]), appData);
            var selectedTag = new TagVM { Id = 5, Name = "Echo", HexCode = "#555555" };

            if (editMode)
            {
                vm.InitializeView(new TransactionVM
                {
                    Id = 15,
                    Type = TransactionType.Expense,
                    SourceAccountId = source.Id,
                    Account = source,
                    Tag = selectedTag
                });
                vm.BeginEditingViewedTransactionAsync().GetAwaiter().GetResult();
                Assert.Equal("Modify Transaction", vm.PopupTitle);
            }
            else
            {
                vm.SelectedTag = selectedTag;
            }

            vm.EnsureTagsLoadedAsync().GetAwaiter().GetResult();

            Assert.Equal([5, 1, 2, 3, 4], vm.VisibleTags.Concat(vm.OverflowTags).Select(tag => tag.Id));
            Assert.Equal(5, vm.SelectedTag?.Id);
        });
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public enum TransactionKind
    {
        Expense,
        Income,
        Goal
    }
}
