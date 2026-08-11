using System.Collections.ObjectModel;
using Fluxo.Helpers.Settings;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.DataModels.Messages;
using Fluxo.Resources.Resources.Messages;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Popups.Settings;
using Fluxo.Helpers.Popups;

namespace Fluxo.ViewModels.Shell.QuickSetupWizard;

public partial class QuickSetupWizardRecurringTransactionsVM : ObservableObject, IDisposable
{
    private const string DefaultTagColor = "#75B798";

    private readonly IAppDataService _appData;
    private readonly IMessenger _messenger;
    private QuickSetupWizardAccountsVM? _accounts;
    private readonly Dictionary<int, QuickSetupWizardDraftRecurringTransaction> _draftExpenses = [];
    private readonly HashSet<int> _removedPersistedIds = [];
    private readonly Dictionary<int, TagVM> _tagCatalog = [];
    private int _nextTemporaryId = -1;
    private int _nextDraftTagId = -1;
    private bool _isLoaded;
    private bool _isDisposed;

    [ObservableProperty] private bool _isStep3Active;

    public QuickSetupWizardRecurringTransactionsVM(
        IAppDataService appData,
        IMessenger messenger)
    {
        _appData = appData;
        _messenger = messenger;
        _messenger.Register<QuickSetupWizardRecurringTransactionsVM, RecurringDraftSaveRequestedMessage>(this,
            static (recipient, message) => message.Reply(recipient.SaveDraftRecurringTransactionAsync(message.Input)));
    }

    public ObservableCollection<QuickSetupWizardRecurringTransactionItemVM> RecurringTransactions { get; } = [];

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _messenger.Unregister<RecurringDraftSaveRequestedMessage>(this);
    }

    public void SetAccounts(QuickSetupWizardAccountsVM accounts)
    {
        _accounts = accounts;
    }

    public TransactionPopupRequest CreateAddViewModel()
    {
        return new TransactionPopupRequest
        {
            Kind = TransactionPopupRequestKind.RecurringDraft,
            Accounts = GetAccountsOrThrow().BuildAccountOptions(),
            UseRecurringDraftMessages = true
        };
    }

    public async Task<TransactionPopupRequest> CreateEditViewModelAsync(int id)
    {
        if (!_isLoaded)
            await LoadDraftExpensesAsync();

        var accounts = GetAccountsOrThrow().BuildAccountOptions();

        if (_draftExpenses.TryGetValue(id, out var draft))
        {
            return new TransactionPopupRequest
            {
                Kind = TransactionPopupRequestKind.RecurringDraft,
                Accounts = accounts,
                UseRecurringDraftMessages = true,
                RecurringDraft = new RecurringDraftSnapshot(
                draft.Id > 0 ? draft.Id : null,
                draft.Type,
                draft.Name,
                draft.Amount,
                draft.RecurringPeriod,
                draft.RecurringTime,
                draft.AccountId,
                draft.Category,
                draft.TagId > 0 ? draft.TagId : null,
                null)
            };
        }

        return new TransactionPopupRequest
        {
            Kind = TransactionPopupRequestKind.EditRecurringTransaction,
            Accounts = accounts,
            UseRecurringDraftMessages = true,
            RecurringTransactionId = id
        };
    }

    public Task DeleteAsync(int id)
    {
        if (id > 0)
            _removedPersistedIds.Add(id);

        _draftExpenses.Remove(id);
        RefreshProjectionAndPublish();
        return Task.CompletedTask;
    }

    public async Task RefreshAsync()
    {
        if (!_isLoaded)
            await LoadDraftExpensesAsync();

        RefreshProjectionAndPublish();
    }

    public async Task ApplyAsync(IAppDataService appData, IReadOnlyDictionary<int, int> sourceIdMap)
    {
        var existingRecurring = (await appData.GetRecurringTransactionsAsync())
            .ToDictionary(expense => expense.Id);

        foreach (var draft in _draftExpenses.Values.OrderBy(expense => expense.Id))
        {
            var mappedSourceId = sourceIdMap.TryGetValue(draft.AccountId, out var mapped)
                ? mapped
                : draft.AccountId;
            if (mappedSourceId <= 0)
                continue;

            var tagId = await ResolveTagIdAsync(appData, draft);

            if (draft.Id > 0 && existingRecurring.TryGetValue(draft.Id, out var persisted))
            {
                persisted.Name = draft.Name;
                persisted.Amount = draft.Amount;
                persisted.SourceId = mappedSourceId;
                persisted.TagId = tagId;
                persisted.RecurringPeriod = draft.RecurringPeriod;
                persisted.RecurringTime = draft.RecurringTime;
                persisted.EndDate = draft.EndDate;
                persisted.Category = draft.Type == RecurringTransactionType.Income
                    ? ExpenseCategory.Excluded
                    : draft.Category;
                appData.UpdateRecurringTransaction(persisted);
            }
            else
            {
                await appData.AddRecurringTransactionAsync(new RecurringTransaction
                {
                    Name = draft.Name,
                    Amount = draft.Amount,
                    RecurringPeriod = draft.RecurringPeriod,
                    RecurringTime = draft.RecurringTime,
                    Type = draft.Type,
                    Category = draft.Type == RecurringTransactionType.Income
                        ? ExpenseCategory.Excluded
                        : draft.Category,
                    SourceId = mappedSourceId,
                    TagId = tagId,
                    IsEnabled = true,
                    EndDate = draft.EndDate
                });
            }
        }

        foreach (var removedId in _removedPersistedIds)
        {
            if (existingRecurring.TryGetValue(removedId, out var existing))
                appData.RemoveRecurringTransaction(existing);
        }
    }

    private void PublishSnapshot()
    {
        _messenger.Send(new QuickSetupWizardRecurringTransactionsChangedMessage(
            new QuickSetupWizardRecurringTransactionsChanged(
                RecurringTransactions.Count,
                RecurringTransactions.Sum(expense => expense.Amount))));
    }

    private async Task LoadDraftExpensesAsync()
    {
        var persistedTags = await _appData.GetTagsAsync();
        _tagCatalog.Clear();
        foreach (var tag in persistedTags.Where(tag => !tag.IsSystemTag))
        {
            _tagCatalog[tag.Id] = new TagVM
            {
                Id = tag.Id,
                Name = tag.Name,
                HexCode = tag.HexCode,
                IsSystemTag = tag.IsSystemTag
            };
        }

        var tagNamesById = _tagCatalog.Values.ToDictionary(tag => tag.Id, tag => tag.Name);
        var persistedExpenses = await _appData.GetRecurringTransactionsAsync();

        _draftExpenses.Clear();

        foreach (var expense in persistedExpenses.Where(item => item.Type is RecurringTransactionType.Expense or RecurringTransactionType.Income))
        {
            _draftExpenses[expense.Id] = new QuickSetupWizardDraftRecurringTransaction(
                expense.Id,
                expense.Type,
                expense.Name,
                expense.Amount,
                expense.Type == RecurringTransactionType.Income
                    ? ExpenseCategory.Excluded
                    : expense.Category,
                expense.SourceId,
                expense.RecurringPeriod,
                expense.RecurringTime,
                expense.TagId ?? 0,
                expense.TagId.HasValue && tagNamesById.TryGetValue(expense.TagId.Value, out var tagName)
                    ? tagName
                    : "General",
                expense.IsEnabled,
                expense.EndDate);
        }

        _removedPersistedIds.Clear();
        _nextTemporaryId = -1;
        _nextDraftTagId = -1;
        _isLoaded = true;
    }

    private Task<TransactionPopupSubmissionResult> SaveDraftRecurringTransactionAsync(
        RecurringDraftSaveInput input)
    {
        if (input.Type is not (RecurringTransactionType.Expense or RecurringTransactionType.Income))
        {
            return Task.FromResult(TransactionPopupSubmissionResult.Failure(
                "Startup Wizard supports recurring income and expenses only."));
        }

        var id = input.EditingRecurringTransactionId ?? _nextTemporaryId--;
        var tagName = ResolveDraftTagName(input.TagId);

        _draftExpenses[id] = new QuickSetupWizardDraftRecurringTransaction(
            id,
            input.Type,
            input.Name,
            input.Amount,
            input.Type == RecurringTransactionType.Income
                ? ExpenseCategory.Excluded
                : input.Category ?? ExpenseCategory.Needs,
            input.AccountId,
            input.RecurringPeriod,
            input.RecurringTime,
            input.TagId ?? 0,
            tagName,
            true,
            input.EndDate);

        if (id > 0)
            _removedPersistedIds.Remove(id);

        RefreshProjectionAndPublish();
        return Task.FromResult(TransactionPopupSubmissionResult.Success());
    }

    private string ResolveDraftTagName(int? tagId)
    {
        if (tagId.HasValue && _tagCatalog.TryGetValue(tagId.Value, out var tag))
            return tag.Name;

        return "General";
    }

    private async Task<int> ResolveTagIdAsync(IAppDataService appData, QuickSetupWizardDraftRecurringTransaction draft)
    {
        if (draft.TagId > 0)
        {
            var existingById = await appData.GetTagByIdAsync(draft.TagId);
            if (existingById is not null && !existingById.IsSystemTag)
                return existingById.Id;
        }

        var desiredTagName = string.IsNullOrWhiteSpace(draft.TagName)
            ? "General"
            : draft.TagName.Trim();

        var existingTags = await appData.GetTagsAsync();
        var existingByName = existingTags.FirstOrDefault(tag =>
            !tag.IsSystemTag &&
            string.Equals(tag.Name, desiredTagName, StringComparison.OrdinalIgnoreCase));
        if (existingByName is not null)
            return existingByName.Id;

        var createdTag = new Tag
        {
            Name = desiredTagName,
            HexCode = DefaultTagColor
        };
        await appData.AddTagAsync(createdTag);
        await appData.SaveChangesAsync();
        return createdTag.Id;
    }

    private Task<SettingsOperationResult> CreateDraftTagAsync(string name, string hexCode)
    {
        var trimmedName = (name ?? string.Empty).Trim();
        var normalizedHexCode = NormalizeHexColor(hexCode);

        if (trimmedName.Length == 0)
            return Task.FromResult(SettingsOperationResult.Failure("Please enter a tag name."));

        if (!IsHexColor(normalizedHexCode))
            return Task.FromResult(SettingsOperationResult.Failure("Please choose a valid tag color."));

        if (_tagCatalog.Values.Any(tag => string.Equals(tag.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult(SettingsOperationResult.Failure($"A tag named \"{trimmedName}\" already exists."));

        _tagCatalog[_nextDraftTagId] = new TagVM
        {
            Id = _nextDraftTagId,
            Name = trimmedName,
            HexCode = normalizedHexCode,
            IsSystemTag = false
        };
        _nextDraftTagId--;

        return Task.FromResult(SettingsOperationResult.Success());
    }

    private IReadOnlyList<TagVM> BuildTagOptions()
    {
        return _tagCatalog.Values
            .Where(tag => !tag.IsSystemTag)
            .OrderBy(tag => tag.Name)
            .Select(tag => new TagVM
            {
                Id = tag.Id,
                Name = tag.Name,
                HexCode = tag.HexCode,
                IsSystemTag = tag.IsSystemTag
            })
            .ToList();
    }

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
               normalized.Skip(1).All(static character =>
                   char.IsDigit(character) || (character >= 'A' && character <= 'F'));
    }

    private void RefreshProjectionAndPublish()
    {
        var accountsVm = GetAccountsOrThrow();

        QuickSetupWizardShared.ReplaceCollection(
            RecurringTransactions,
            _draftExpenses.Values
                .OrderBy(expense => expense.Name)
                .Select(expense => new QuickSetupWizardRecurringTransactionItemVM(
                    expense,
                    accountsVm.ResolveSourceName(expense.AccountId))));

        PublishSnapshot();
    }

    private QuickSetupWizardAccountsVM GetAccountsOrThrow()
    {
        return _accounts
               ?? throw new InvalidOperationException("Startup wizard accounts were not configured.");
    }
}

