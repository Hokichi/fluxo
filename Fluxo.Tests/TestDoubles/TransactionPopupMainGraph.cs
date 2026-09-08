using Fluxo.Core.Interfaces.Services;
using Fluxo.ViewModels.Shell.Main;

namespace Fluxo.Tests.TestDoubles;

internal sealed record TransactionPopupMainGraph(
    MainVM Main,
    IAppDataService BudgetAppData,
    IAppDataService SpentAppData,
    TaskCompletionSource LedgerFirstReload,
    TaskCompletionSource LedgerSecondReload,
    Func<int> LedgerCalls);
