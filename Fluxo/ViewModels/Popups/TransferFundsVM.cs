using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.History;
using Fluxo.Services.Logging;
using Fluxo.Services.Notifications;
using Fluxo.ViewModels.Entities;
using Fluxo.Helpers.Popups;
using Fluxo.ViewModels.Shell;
using MainVM = Fluxo.ViewModels.Shell.Main.MainVM;

namespace Fluxo.ViewModels.Popups;

public partial class TransferFundsVM : ObservableObject
{
    private readonly MainVM _mainViewModel;
    private readonly int _sourceAccountId;
    private readonly IAppDataService _appData;

    [ObservableProperty] private decimal _amountText;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string _noteText = string.Empty;
    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private AccountVM? _selectedTarget;

    public TransferFundsVM(MainVM mainViewModel, AccountVM source, IAppDataService appData)
    {
        _mainViewModel = mainViewModel;
        _sourceAccountId = source.Id;
        _appData = appData;
        SourceName = source.Name;
        TargetsView = AccountComboBoxViewFactory.CreateGroupedByTypeThenName(
            Targets,
            nameof(AccountVM.TypeDisplayName),
            nameof(AccountVM.AccountType),
            nameof(AccountVM.Name));

        var candidateTargets = _mainViewModel.BudgetPanel.Accounts
            .Where(account => account.Id != source.Id && account.IsEnabled)
            .OrderBy(account => account.AccountType)
            .ThenBy(account => account.Name)
            .ToList();

        foreach (var candidate in candidateTargets)
            Targets.Add(candidate);

        SelectedTarget = Targets.FirstOrDefault();
    }

    public string PopupTitle => "Transfer Funds";

    public string SourceName { get; }

    public ObservableCollection<AccountVM> Targets { get; } = [];
    public ICollectionView TargetsView { get; }

    public async Task<TransferFundsResult> SaveAsync()
    {
        if (IsSaving)
            return TransferFundsResult.Failure("This transfer is already being saved.");

        if (!TryBuildInput(out var input, out var validationMessage))
            return TransferFundsResult.Failure(validationMessage);

        IsSaving = true;

        try
        {
            var source = await _appData.GetAccountByIdAsync(_sourceAccountId);
            if (source is null)
                return TransferFundsResult.Failure("Unable to load the source account.");

            var target = await _appData.GetAccountByIdAsync(input.TargetAccountId);
            if (target is null)
                return TransferFundsResult.Failure("Please choose a valid destination account.");

            var tag = await ResolveTransferTagAsync();
            if (tag is null)
                return TransferFundsResult.Failure("Add at least one expense tag before creating a transfer.");

            var expense = new Transaction
            {
                Type = TransactionType.Expense,
                Name = $"Transfer to {target.Name}",
                Amount = input.Amount,
                OccurredOn = input.Date,
                Notes = BuildExpenseNote(target.Name, input.Note),
                ExpenseCategory = ExpenseCategory.Savings,
                SourceAccountId = source.Id,
                TagId = tag.Id
            };

            var income = new Transaction
            {
                Type = TransactionType.Income,
                Name = BuildIncomeName(source.Name),
                Amount = input.Amount,
                OccurredOn = input.Date,
                Notes = input.Note,
                SourceAccountId = target.Id
            };

            await _appData.AddTransactionAsync(expense);
            await _appData.AddTransactionAsync(income);

            ApplyExpenseToAccount(source, input.Amount);
            ApplyIncomeToAccount(target, input.Amount);

            _appData.UpdateAccount(source);
            _appData.UpdateAccount(target);

            await _appData.SaveChangesAsync();

            WeakReferenceMessenger.Default.Send(new RecordLogMemoryMessage(
                new CompositeLogMemoryAction(
                    "Transfer funds",
                    [
                        new AddTransactionMemoryAction(TransactionMemorySnapshot.Create(expense)),
                        new AddTransactionMemoryAction(TransactionMemorySnapshot.Create(income))
                    ])));

            WeakReferenceMessenger.Default.Send(new DashboardDataInvalidatedMessage(
                DashboardDataInvalidationScope.Budget | DashboardDataInvalidationScope.Notifications));

            await _mainViewModel.ReloadCurrentDataAsync();
            FloatingNotificationPublisher.Success(
                target.Name, $"{input.Amount:N2} was transferred successfully.", true, "Credited");
            return TransferFundsResult.Success();
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(WeakReferenceMessenger.Default, exception, "save transfer");
            return TransferFundsResult.Failure(string.Empty);
        }
        finally
        {
            IsSaving = false;
        }
    }

    public bool HasValidEntryToPersistOnClose()
    {
        return TryBuildInput(out _, out _);
    }

    private async Task<Tag?> ResolveTransferTagAsync()
    {
        var tags = await _appData.GetTagsAsync();
        return tags
            .OrderByDescending(tag => string.Equals(tag.Name, "Transfer", StringComparison.OrdinalIgnoreCase))
            .ThenBy(tag => tag.Name)
            .FirstOrDefault();
    }

    private bool TryBuildInput(out TransferFundsInput input, out string validationMessage)
    {
        input = default;
        validationMessage = string.Empty;

        var failures = new List<string>();
        if (AmountText <= 0m)
            failures.Add("Please enter a valid transfer amount greater than zero.");
        if (SelectedTarget is null)
            failures.Add("Please choose where the funds should go.");
        if (failures.Count > 0)
        {
            validationMessage = string.Join(Environment.NewLine, failures);
            return false;
        }

        input = new TransferFundsInput(AmountText, SelectedTarget!.Id, SelectedDate.Date, NoteText.Trim());
        return true;
    }

    private static string BuildExpenseNote(string targetName, string note)
    {
        var transferLine = $"Transfer to {targetName}";
        return string.IsNullOrWhiteSpace(note)
            ? transferLine
            : $"{transferLine}\n{note.Trim()}";
    }

    private static string BuildIncomeName(string sourceName)
    {
        return $"Transfer from {sourceName}";
    }

    private static void ApplyExpenseToAccount(Account account, decimal amount)
    {
        if (account.AccountType == AccountType.Credit)
        {
            account.SpentAmount += amount;
            return;
        }

        account.Balance -= amount;
    }

    private static void ApplyIncomeToAccount(Account account, decimal amount)
    {
        if (account.AccountType == AccountType.Credit)
        {
            account.SpentAmount = Math.Max(0m, account.SpentAmount - amount);
            return;
        }

        account.Balance += amount;
    }

    public readonly record struct TransferFundsResult(bool IsSuccess, string? ErrorMessage)
    {
        public static TransferFundsResult Success()
        {
            return new TransferFundsResult(true, null);
        }

        public static TransferFundsResult Failure(string? errorMessage)
        {
            return new TransferFundsResult(false, errorMessage);
        }
    }

    private readonly record struct TransferFundsInput(
        decimal Amount,
        int TargetAccountId,
        DateTime Date,
        string Note);
}
