using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces.Services;
using Fluxo.DataModels.Popups.GlobalSearch;

namespace Fluxo.ViewModels.Popups;

public sealed class QuickAccessVM : ObservableObject
{
    private readonly IAppDataService _appData;
    private HashSet<GlobalSearchFeatureTarget> _baselineDisabledTargets = [];
    private bool _isEditing;
    private UserSettings? _setting;

    public QuickAccessVM(IAppDataService appData)
    {
        _appData = appData;
        Tiles = CreateTiles();
        TileRows = Tiles
            .Chunk(3)
            .Select(row => (IReadOnlyList<QuickAccessTileVM>)row)
            .ToArray();
    }

    public IReadOnlyList<QuickAccessTileVM> Tiles { get; }
    public IReadOnlyList<IReadOnlyList<QuickAccessTileVM>> TileRows { get; }

    public bool IsEditing
    {
        get => _isEditing;
        private set
        {
            if (!SetProperty(ref _isEditing, value))
                return;

            foreach (var tile in Tiles)
                tile.IsEditing = value;
            OnPropertyChanged(nameof(EditButtonText));
        }
    }

    public bool HasPendingChanges => !_baselineDisabledTargets.SetEquals(CurrentDisabledTargets());
    public string EditButtonText => IsEditing ? "Done" : "Edit";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _setting = await _appData.GetUserSettingByNameAsync(
            UserSettingNames.DisabledQuickAccessTiles,
            cancellationToken);
        var disabledTargets = ParseDisabledTargets(_setting?.Value);
        foreach (var tile in Tiles)
            tile.IsUserEnabled = !disabledTargets.Contains(tile.Target);

        _baselineDisabledTargets = disabledTargets;
        IsEditing = false;
        OnPropertyChanged(nameof(HasPendingChanges));
    }

    public void BeginEditing()
    {
        IsEditing = true;
    }

    public void Toggle(QuickAccessTileVM tile)
    {
        if (!IsEditing || !Tiles.Contains(tile))
            return;

        tile.IsUserEnabled = !tile.IsUserEnabled;
        OnPropertyChanged(nameof(HasPendingChanges));
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var disabledTargets = CurrentDisabledTargets();
        if (_baselineDisabledTargets.SetEquals(disabledTargets))
        {
            IsEditing = false;
            return;
        }

        var value = string.Join(",", Tiles
            .Where(tile => disabledTargets.Contains(tile.Target))
            .Select(tile => tile.Target.ToString()));
        if (_setting is null)
        {
            _setting = new UserSettings
            {
                Name = UserSettingNames.DisabledQuickAccessTiles,
                Value = value
            };
            await _appData.AddUserSettingAsync(_setting, cancellationToken);
        }
        else
        {
            _setting.Value = value;
            _appData.UpdateUserSetting(_setting);
        }

        await _appData.SaveChangesAsync(cancellationToken);
        _baselineDisabledTargets = disabledTargets;
        IsEditing = false;
        OnPropertyChanged(nameof(HasPendingChanges));
    }

    public void SetSufficientFundsGate(bool isLocked)
    {
        foreach (var tile in Tiles)
            tile.IsOperationallyAvailable = !tile.RequiresSufficientFunds || !isLocked;
    }

    private HashSet<GlobalSearchFeatureTarget> ParseDisabledTargets(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var catalogTargets = Tiles.Select(tile => tile.Target).ToHashSet();
        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => Enum.TryParse<GlobalSearchFeatureTarget>(part, out var target)
                ? target
                : (GlobalSearchFeatureTarget?)null)
            .Where(target => target.HasValue && catalogTargets.Contains(target.Value))
            .Select(target => target!.Value)
            .ToHashSet();
    }

    private HashSet<GlobalSearchFeatureTarget> CurrentDisabledTargets() =>
        Tiles.Where(tile => !tile.IsUserEnabled).Select(tile => tile.Target).ToHashSet();

    private static IReadOnlyList<QuickAccessTileVM> CreateTiles() =>
    [
        Tile(GlobalSearchFeatureTarget.NewTransaction, "New Transaction", "Add income or expense quickly.", "InvoiceLight", true),
        Tile(GlobalSearchFeatureTarget.ViewAccounts, "View Accounts", "Review accounts and wallets.", "Bank", false),
        Tile(GlobalSearchFeatureTarget.NewAccount, "New Account", "Add a new account or wallet.", "Bank", false),
        Tile(GlobalSearchFeatureTarget.NewRecurringTransaction, "New Recurring Transaction", "Create a repeating transaction.", "RegularRepeat", true),
        Tile(GlobalSearchFeatureTarget.NewSavingGoal, "New Saving Goal", "Set a target and track progress.", "BullseyeSolid", true),
        Tile(GlobalSearchFeatureTarget.NewTag, "New Tag", "Create a transaction tag.", "SolidPriceTagAlt", false),
        Tile(GlobalSearchFeatureTarget.PlanningReport, "Planning Report", "Plan future cash movement.", "PlusSolid", true),
        Tile(GlobalSearchFeatureTarget.BudgetForecast, "Budget Forecast", "Project balances and budget.", "CalendarFuture", true),
        Tile(GlobalSearchFeatureTarget.SearchEverything, "Search Everything", "Find features and financial records.", "MagnifyingGlass", true),
        Tile(GlobalSearchFeatureTarget.DataManagement, "Data Backup/Restore", "Back up or restore local data.", "ContentSaveMove", false),
        Tile(GlobalSearchFeatureTarget.LockApplication, "Lock Application", "Protect Fluxo until unlocked.", "Lock", false),
        Tile(GlobalSearchFeatureTarget.RunQuickSetup, "Run Quick Setup", "Revisit guided setup.", "RocketSolid", false),
        Tile(GlobalSearchFeatureTarget.Hotkeys, "Hotkeys Overview", "Review keyboard shortcuts.", "KeyboardBoxFill", false),
        Tile(GlobalSearchFeatureTarget.CheckForUpdates, "Check for Updates", "Look for a newer version.", "UpdateOutline", false)
    ];

    private static QuickAccessTileVM Tile(
        GlobalSearchFeatureTarget target,
        string title,
        string description,
        string iconResourceKey,
        bool requiresSufficientFunds) =>
        new(target, title, description, ResolveIcon(iconResourceKey), requiresSufficientFunds);

    private static Geometry ResolveIcon(string resourceKey) =>
        Application.Current?.FindResource(resourceKey) as Geometry
        ?? throw new InvalidOperationException($"Quick Access icon resource '{resourceKey}' was not found.");
}
