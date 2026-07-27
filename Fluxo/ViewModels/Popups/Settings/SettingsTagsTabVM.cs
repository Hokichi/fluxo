using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Budgeting;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.Logging;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Shell;
using MainVM = Fluxo.ViewModels.Shell.Main.MainVM;
using System.Globalization;

using Fluxo.DataModels.Popups.Settings.SettingsTagsTab;
namespace Fluxo.ViewModels.Popups.Settings;


public partial class SettingsTagsTabVM : ObservableObject
{
    private readonly MainVM _mainViewModel;
    private readonly IMessenger _messenger;
    private readonly IAppDataService _appData;

    public SettingsTagsTabVM(MainVM mainViewModel, IAppDataService appData, IMessenger? messenger = null)
    {
        _mainViewModel = mainViewModel;
        _appData = appData;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
    }

    public ObservableCollection<SettingsTagCardVM> Tags { get; } = [];
    public bool HasTags => Tags.Count > 0;

    public async Task LoadAsync()
    {
        await RefreshTagsAsync();
    }

    public void RequestAddTagDialog()
    {
        _messenger.Send(new SettingsDialogRequestedMessage(
            new SettingsDialogRequest(
                SettingsDialogRequestType.AddTag,
                new SettingsTagDialogRequest(CreateAddTagViewModel(), CreateTagAsync))));
    }

    public AddTagVM CreateAddTagViewModel()
    {
        return new AddTagVM();
    }

    public async Task<AddTagVM?> CreateEditTagViewModelAsync(int tagId)
    {
        var tag = await _appData.GetTagByIdAsync(tagId);
        if (tag is null || tag.IsSystemTag)
            return null;

        return new AddTagVM(tag.Id, tag.Name, tag.HexCode, tag.SpendingLimit);
    }

    public async Task OpenEditTagAsync(int tagId)
    {
        var viewModel = await CreateEditTagViewModelAsync(tagId);
        if (viewModel is null)
            return;

        _messenger.Send(new SettingsDialogRequestedMessage(
            new SettingsDialogRequest(
                SettingsDialogRequestType.AddTag,
                new SettingsTagDialogRequest(viewModel, (name, hexCode, spendingLimit) => UpdateTagAsync(tagId, name, hexCode, spendingLimit)))));

        await RefreshTagsAsync();
    }

    public async Task RefreshTagsAsync()
    {
        var tags = await _appData.GetTagsByCountDescendingAsync();
        var transactions = await _appData.GetTransactionsAsync();
        var allocation = await _appData.GetBudgetAllocationAsync();

        SettingsShared.ReplaceCollection(Tags, CreateCards(tags, transactions, allocation, DateTime.Today));
        OnPropertyChanged(nameof(HasTags));
    }

    internal static IReadOnlyList<SettingsTagCardVM> CreateCards(
        IReadOnlyList<(Tag Tag, int Count)> tags,
        IReadOnlyList<Transaction> transactions,
        BudgetAllocation allocation,
        DateTime today)
    {
        var period = BudgetAllocationCalculator.ResolveCurrentPeriod(
            allocation.AllocationPeriod,
            today,
            allocation.PeriodStart);
        var spendingByTag = transactions
            .Where(transaction =>
                transaction.Type == TransactionType.Expense &&
                !transaction.IsForDeletion &&
                !transaction.IsExcludedFromBudget &&
                transaction.OccurredOn.Date >= period.Start &&
                transaction.OccurredOn.Date <= period.End)
            .Select(transaction => new
            {
                TagId = transaction.TagId ?? transaction.Tag?.Id,
                transaction.Amount
            })
            .Where(item => item.TagId.HasValue)
            .GroupBy(item => item.TagId!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));

        return tags
            .Where(item => !item.Tag.IsSystemTag)
            .Select(item => SettingsTagCardVM.Create(
                item.Tag,
                spendingByTag.GetValueOrDefault(item.Tag.Id)))
            .ToList();
    }

    public async Task<SettingsOperationResult> CreateTagAsync(string name, string hexCode, string spendingLimitText)
    {
        var trimmedName = (name ?? string.Empty).Trim();
        var normalizedHexCode = NormalizeHexColor(hexCode);

        if (trimmedName.Length == 0)
            return SettingsOperationResult.Failure("Please enter a tag name.");

        if (!IsHexColor(normalizedHexCode))
            return SettingsOperationResult.Failure("Please choose a valid tag color.");

        if (!TryParseSpendingLimit(spendingLimitText, out var spendingLimit, out var spendingLimitError))
            return SettingsOperationResult.Failure(spendingLimitError);

        try
        {
            var existingTags = await _appData.GetTagsAsync();
            if (existingTags.Any(tag => string.Equals(tag.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
                return SettingsOperationResult.Failure($"A tag named \"{trimmedName}\" already exists.");

            await _appData.AddTagAsync(new Tag
            {
                Name = trimmedName,
                HexCode = normalizedHexCode,
                SpendingLimit = spendingLimit
            });

            await _appData.SaveChangesAsync();
            _messenger.Send(new SettingsDataChangedMessage(SettingsDataChangedScope.Tags));
            _messenger.Send(new DashboardDataInvalidatedMessage(DashboardDataInvalidationScope.All));
            await _mainViewModel.ReloadCurrentDataAsync();
            await RefreshTagsAsync();

            return SettingsOperationResult.Success();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Unable to create this tag.");
            return SettingsOperationResult.Failure(
                FluxoLogManager.CreateFailureMessage("create tag"));
        }
    }

    public Task<SettingsOperationResult> CreateTagAsync(string name, string hexCode) =>
        CreateTagAsync(name, hexCode, string.Empty);

    public Task<SettingsOperationResult> DeleteTagAsync(SettingsTagCardVM tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return DeleteTagAsync(tag.Id);
    }

    public Task<SettingsOperationResult> DeleteTagAsync(TagVM tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return DeleteTagAsync(tag.Id);
    }

    private async Task<SettingsOperationResult> DeleteTagAsync(int tagId)
    {
        try
        {
            var persistedTag = await _appData.GetTagByIdAsync(tagId);
            if (persistedTag is null)
                return SettingsOperationResult.Failure("That tag could not be found anymore.");

            var allTransactions = await _appData.GetTransactionsAsync();
            var linkedExpenses = allTransactions.Where(transaction => transaction.TagId == persistedTag.Id).ToList();
            if (linkedExpenses.Count > 0)
                return SettingsOperationResult.Failure(
                    $"{persistedTag.Name} is still assigned to one or more expenses, so it can't be deleted yet.");

            _appData.RemoveTag(persistedTag);
            await _appData.SaveChangesAsync();

            _messenger.Send(new SettingsDataChangedMessage(SettingsDataChangedScope.Tags));
            _messenger.Send(new DashboardDataInvalidatedMessage(DashboardDataInvalidationScope.All));
            await _mainViewModel.ReloadCurrentDataAsync();
            await RefreshTagsAsync();

            return SettingsOperationResult.Success();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Unable to delete this tag.");
            return SettingsOperationResult.Failure(
                FluxoLogManager.CreateFailureMessage("delete tag"));
        }
    }

    public async Task<SettingsOperationResult> UpdateTagAsync(int tagId, string name, string hexCode, string spendingLimitText)
    {
        var trimmedName = (name ?? string.Empty).Trim();
        var normalizedHexCode = NormalizeHexColor(hexCode);

        if (trimmedName.Length == 0)
            return SettingsOperationResult.Failure("Please enter a tag name.");

        if (!IsHexColor(normalizedHexCode))
            return SettingsOperationResult.Failure("Please choose a valid tag color.");

        if (!TryParseSpendingLimit(spendingLimitText, out var spendingLimit, out var spendingLimitError))
            return SettingsOperationResult.Failure(spendingLimitError);

        try
        {
            var tag = await _appData.GetTagByIdAsync(tagId);
            if (tag is null)
                return SettingsOperationResult.Failure("That tag could not be found anymore.");

            if (tag.IsSystemTag)
                return SettingsOperationResult.Failure("System tags can't be edited.");

            var existingTags = await _appData.GetTagsAsync();
            if (existingTags.Any(tag =>
                    tag.Id != tag.Id &&
                    string.Equals(tag.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
                return SettingsOperationResult.Failure($"A tag named \"{trimmedName}\" already exists.");

            var hasNameChanged = !string.Equals(tag.Name, trimmedName, StringComparison.Ordinal);
            var hasHexChanged = !string.Equals(tag.HexCode, normalizedHexCode, StringComparison.OrdinalIgnoreCase);
            var hasSpendingLimitChanged = tag.SpendingLimit != spendingLimit;
            if (!hasNameChanged && !hasHexChanged && !hasSpendingLimitChanged)
                return SettingsOperationResult.Success();

            tag.Name = trimmedName;
            tag.HexCode = normalizedHexCode;
            tag.SpendingLimit = spendingLimit;
            _appData.UpdateTag(tag);
            await _appData.SaveChangesAsync();

            _messenger.Send(new SettingsDataChangedMessage(SettingsDataChangedScope.Tags));
            _messenger.Send(new DashboardDataInvalidatedMessage(DashboardDataInvalidationScope.All));
            await _mainViewModel.ReloadCurrentDataAsync();
            await RefreshTagsAsync();

            return SettingsOperationResult.Success();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Unable to update this tag.");
            return SettingsOperationResult.Failure(
                FluxoLogManager.CreateFailureMessage("update tag"));
        }
    }

    public Task<SettingsOperationResult> UpdateTagAsync(int tagId, string name, string hexCode) =>
        UpdateTagAsync(tagId, name, hexCode, string.Empty);

    private static string NormalizeHexColor(string hexCode)
    {
        var normalized = (hexCode ?? string.Empty).Trim().TrimStart('#').ToUpperInvariant();
        return $"#{normalized}";
    }

    private static bool IsHexColor(string hexCode)
    {
        var normalized = NormalizeHexColor(hexCode);
        return normalized.Length == 7 &&
               normalized[0] == '#' &&
               normalized.Skip(1).All(static character => char.IsDigit(character) ||
                                                          (character >= 'A' && character <= 'F'));
    }

    private static bool TryParseSpendingLimit(string? value, out decimal? spendingLimit, out string errorMessage)
    {
        spendingLimit = null;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ||
            parsed < 0m)
        {
            errorMessage = "Please enter a valid spending limit.";
            return false;
        }

        spendingLimit = parsed == 0m ? null : parsed;
        return true;
    }
}

public sealed class SettingsTagCardVM
{
    public int Id { get; private init; }
    public string Name { get; private init; } = string.Empty;
    public string HexCode { get; private init; } = string.Empty;
    public decimal Spent { get; private init; }
    public decimal? SpendingLimit { get; private init; }
    public bool HasSpendingLimit => SpendingLimit is > 0m;
    public string SpentText { get; private init; } = string.Empty;
    public string LimitText { get; private init; } = string.Empty;
    public string RemainderText { get; private init; } = string.Empty;
    public string PercentageText { get; private init; } = string.Empty;
    public double ProgressPercentage { get; private init; }
    public SettingsTagSpendingState SpendingState { get; private init; }

    internal static SettingsTagCardVM Create(Tag tag, decimal spent)
    {
        if (tag.SpendingLimit is not > 0m)
        {
            return new SettingsTagCardVM
            {
                Id = tag.Id,
                Name = tag.Name,
                HexCode = tag.HexCode,
                Spent = spent,
                SpendingLimit = null,
                SpentText = FormatMoney(spent),
                PercentageText = "∞",
                ProgressPercentage = 100d,
                SpendingState = SettingsTagSpendingState.Success
            };
        }

        var limit = tag.SpendingLimit.Value;
        var rawPercentage = spent / limit * 100m;
        var percentage = (int)Math.Round(rawPercentage, MidpointRounding.AwayFromZero);
        var state = rawPercentage < 75m
            ? SettingsTagSpendingState.Success
            : rawPercentage <= 100m
                ? SettingsTagSpendingState.Warning
                : SettingsTagSpendingState.Danger;

        return new SettingsTagCardVM
        {
            Id = tag.Id,
            Name = tag.Name,
            HexCode = tag.HexCode,
            Spent = spent,
            SpendingLimit = limit,
            SpentText = FormatMoney(spent),
            LimitText = $"of {FormatMoney(limit)}",
            RemainderText = $"{FormatMoney(Math.Abs(limit - spent))} {(spent <= limit ? "left" : "over")}",
            PercentageText = $"{percentage}%",
            ProgressPercentage = (double)Math.Clamp(rawPercentage, 0m, 100m),
            SpendingState = state
        };
    }

    private static string FormatMoney(decimal value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);
}
