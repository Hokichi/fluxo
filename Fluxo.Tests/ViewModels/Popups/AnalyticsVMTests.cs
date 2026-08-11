using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Fluxo.Core.DTO;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Services.Dialogs;
using Fluxo.Services.Ui;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Shell.Main;
using NSubstitute;
using System.Windows;
using Xunit;
using AnalyticsVM = Fluxo.ViewModels.Shell.Main.AnalyticsVM;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class AnalyticsVMTests
{
    [Fact]
    public void AnalyticsVM_InitialDateRange_CoversSevenInclusiveDatesEndingToday()
    {
        var vm = CreateVm();

        Assert.Equal(DateTime.Today.AddDays(-6), vm.StartDate.Date);
        Assert.Equal(DateTime.Today, vm.EndDate.Date);
    }

    [Fact]
    public void AnalyticsVM_DateRangeOver31Days_AdjustsEndDateAndShowsWarning()
    {
        var vm = CreateVm();

        vm.StartDate = new DateTime(2026, 1, 1);
        vm.EndDate = new DateTime(2026, 2, 20);

        Assert.Equal(new DateTime(2026, 2, 1), vm.EndDate.Date);
        Assert.True(vm.HasDateRangeWarning);
        Assert.Contains("31 days", vm.DateRangeWarningMessage);
    }

    [Fact]
    public void AnalyticsVM_DateRangeOver14Days_RotatesLabelsAndHidesTrendValues()
    {
        var vm = CreateVm();

        vm.StartDate = new DateTime(2026, 1, 1);
        vm.EndDate = new DateTime(2026, 1, 20);
        Assert.True(vm.IsTrendLabelVertical);
        Assert.True(vm.HideTrendValueLabels);

        vm.EndDate = new DateTime(2026, 1, 10);
        Assert.False(vm.IsTrendLabelVertical);
        Assert.False(vm.HideTrendValueLabels);
    }

    [Fact]
    public async Task AnalyticsVM_TrendBarsUseModeSpecificColorFlags_ReturnsVerifiedOutcome()
    {
        var vm = CreateVm();
        vm.StartDate = new DateTime(2026, 1, 1);
        vm.EndDate = new DateTime(2026, 1, 20);
        await vm.LoadAsync();

        Assert.Equal(3, vm.TrendBarItems.Count);
        Assert.All(vm.TrendBarItems, item =>
        {
            Assert.True(item.HideValueText);
            Assert.True(item.RotateLabelVertical);
        });

        Assert.Equal("Thu", vm.TrendBarItems[0].Label);
        Assert.Equal(40m, vm.TrendBarItems[0].Value);
        Assert.Equal(100m, vm.TrendBarItems[0].SecondaryValue);
        Assert.True(vm.TrendBarItems[0].HasSecondaryBar);
        Assert.True(vm.TrendBarItems[0].IsExpenseMode);
        Assert.False(vm.TrendBarItems[0].IsIncomeMode);
        Assert.False(vm.TrendBarItems[0].IsSecondaryExpenseMode);
        Assert.True(vm.TrendBarItems[0].IsSecondaryIncomeMode);

        Assert.Equal("Fri", vm.TrendBarItems[1].Label);
        Assert.Equal(60m, vm.TrendBarItems[1].Value);
        Assert.Equal(160m, vm.TrendBarItems[1].SecondaryValue);
        Assert.True(vm.TrendBarItems[1].HasSecondaryBar);
        Assert.True(vm.TrendBarItems[1].IsExpenseMode);
        Assert.False(vm.TrendBarItems[1].IsIncomeMode);
        Assert.False(vm.TrendBarItems[1].IsSecondaryExpenseMode);
        Assert.True(vm.TrendBarItems[1].IsSecondaryIncomeMode);

        Assert.Equal("Sat", vm.TrendBarItems[2].Label);
        Assert.Equal(55m, vm.TrendBarItems[2].Value);
        Assert.Equal(120m, vm.TrendBarItems[2].SecondaryValue);
        Assert.True(vm.TrendBarItems[2].HasSecondaryBar);
        Assert.True(vm.TrendBarItems[2].IsExpenseMode);
        Assert.False(vm.TrendBarItems[2].IsIncomeMode);
        Assert.False(vm.TrendBarItems[2].IsSecondaryExpenseMode);
        Assert.True(vm.TrendBarItems[2].IsSecondaryIncomeMode);

        vm.SelectedTrendMode = AnalyticsTrendMode.Expenses;
        Assert.Equal(3, vm.TrendBarItems.Count);
        Assert.All(vm.TrendBarItems, item =>
        {
            Assert.False(item.HasSecondaryBar);
            Assert.Equal(0m, item.SecondaryValue);
            Assert.True(item.IsExpenseMode);
            Assert.False(item.IsIncomeMode);
            Assert.False(item.IsSecondaryExpenseMode);
            Assert.False(item.IsSecondaryIncomeMode);
        });

        vm.SelectedTrendMode = AnalyticsTrendMode.Incomes;
        Assert.Equal(3, vm.TrendBarItems.Count);
        Assert.All(vm.TrendBarItems, item =>
        {
            Assert.False(item.HasSecondaryBar);
            Assert.Equal(0m, item.SecondaryValue);
            Assert.False(item.IsExpenseMode);
            Assert.True(item.IsIncomeMode);
            Assert.False(item.IsSecondaryExpenseMode);
            Assert.False(item.IsSecondaryIncomeMode);
        });
    }

    [Fact]
    public async Task AnalyticsVM_TrendScale_UsesRoundedCeilingForSelectedMode()
    {
        var vm = CreateVm();

        await vm.LoadAsync();

        Assert.Equal(new[] { "160", "120", "80", "40" }, vm.TrendGridLineLabels);
        Assert.Equal(
            0.8d,
            vm.TrendBarItems.Max(item => Math.Max(item.BarHeightRatio, item.SecondaryBarHeightRatio)),
            precision: 10);

        vm.SelectedTrendMode = AnalyticsTrendMode.Expenses;

        Assert.Equal(new[] { "80", "60", "40", "20" }, vm.TrendGridLineLabels);
        Assert.Equal(0.6d, vm.TrendBarItems.Max(item => item.BarHeightRatio), precision: 10);
    }

    [Fact]
    public async Task AnalyticsVM_TrendTooltips_DoNotIncludeCurrencySymbols()
    {
        var vm = CreateVm();

        await vm.LoadAsync();

        Assert.All(vm.TrendBarItems, item =>
        {
            Assert.DoesNotContain("$", item.ValueText);
            Assert.DoesNotContain("$", item.SecondaryValueText);
        });
    }

    [Fact]
    public async Task AnalyticsVM_ApplyExternalDateRangeWithoutRefresh_UsesRangeOnFirstLoad()
    {
        var service = Substitute.For<IAnalyticsService>();
        service.GetAnalyticsAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AnalyticsDto(
                TotalIncome: 0m,
                TotalExpense: 0m,
                TimeSeries: [],
                CategoryRatio: [],
                TopSpendingTags: [],
                GoalsCreatedInPeriod: [])));
        var vm = new AnalyticsVM(service);

        vm.ApplyExternalDateRange(
            new DateTime(2026, 1, 6),
            new DateTime(2026, 1, 12),
            refresh: false);

        await vm.LoadAsync();

        await service.Received(1).GetAnalyticsAsync(
            new DateOnly(2026, 1, 6),
            new DateOnly(2026, 1, 12),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnalyticsVM_RefreshForOpenAsync_ShowToastFalse_RefreshesAndSettlesWithoutDialogToast()
    {
        var service = CreateAnalyticsService();
        var dialogService = Substitute.For<IDialogService>();
        var uiSettleAwaiter = Substitute.For<IUiSettleAwaiter>();
        var vm = new AnalyticsVM(service, dialogService, uiSettleAwaiter);

        await vm.RefreshForOpenAsync(showToast: false, CancellationToken.None);

        await service.Received(1).GetAnalyticsAsync(
            Arg.Any<DateOnly>(),
            Arg.Any<DateOnly>(),
            Arg.Any<CancellationToken>());
        await uiSettleAwaiter.Received(1).WaitForUiReadyAsync(
            Arg.Any<Window?>(),
            Arg.Any<CancellationToken>());
        await dialogService.DidNotReceive().ShowToastWhileAsync(
            Arg.Any<string>(),
            Arg.Any<Func<Task>>(),
            Arg.Any<Window?>());
    }

    [Fact]
    public async Task AnalyticsVM_RefreshForOpenAsync_ShowToastFalse_CancelsPendingDebounceRefresh()
    {
        var serviceCallCount = 0;
        var service = Substitute.For<IAnalyticsService>();
        service.GetAnalyticsAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref serviceCallCount);
                return Task.FromResult(CreateAnalyticsDto());
            });
        var dialogService = Substitute.For<IDialogService>();
        var uiSettleAwaiter = Substitute.For<IUiSettleAwaiter>();
        var vm = new AnalyticsVM(service, dialogService, uiSettleAwaiter);

        vm.StartDate = new DateTime(2026, 1, 1);
        vm.EndDate = new DateTime(2026, 1, 2);

        await vm.RefreshForOpenAsync(showToast: false, CancellationToken.None);
        await Task.Delay(350);

        await dialogService.DidNotReceive().ShowToastWhileAsync(
            Arg.Any<string>(),
            Arg.Any<Func<Task>>(),
            Arg.Any<Window?>());
        await service.Received(1).GetAnalyticsAsync(
            Arg.Any<DateOnly>(),
            Arg.Any<DateOnly>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(1, Volatile.Read(ref serviceCallCount));
    }

    [Fact]
    public async Task AnalyticsVM_RefreshForOpenAsync_ShowToastTrue_UsesDialogToastWrapper()
    {
        var service = Substitute.For<IAnalyticsService>();
        var dialogService = Substitute.For<IDialogService>();
        var toastDelegateInvoked = false;
        var toastDelegateRunning = false;
        var serviceCalledWhileToastDelegateRunning = false;
        service.GetAnalyticsAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                if (toastDelegateRunning)
                    serviceCalledWhileToastDelegateRunning = true;
                return Task.FromResult(CreateAnalyticsDto());
            });
        dialogService.ShowToastWhileAsync(
                Arg.Any<string>(),
                Arg.Any<Func<Task>>(),
                Arg.Any<Window?>())
            .Returns(async callInfo =>
            {
                toastDelegateInvoked = true;
                toastDelegateRunning = true;
                try
                {
                    await ((Func<Task>)callInfo[1])();
                }
                finally
                {
                    toastDelegateRunning = false;
                }
            });
        var uiSettleAwaiter = Substitute.For<IUiSettleAwaiter>();
        var vm = new AnalyticsVM(service, dialogService, uiSettleAwaiter);

        await vm.RefreshForOpenAsync(showToast: true, CancellationToken.None);

        await dialogService.Received(1).ShowToastWhileAsync(
            Arg.Is<string>(message =>
                message.Contains("Loading analytics", StringComparison.Ordinal)),
            Arg.Any<Func<Task>>(),
            Arg.Any<Window?>());
        await service.Received(1).GetAnalyticsAsync(
            Arg.Any<DateOnly>(),
            Arg.Any<DateOnly>(),
            Arg.Any<CancellationToken>());
        await uiSettleAwaiter.Received(1).WaitForUiReadyAsync(
            Arg.Any<Window?>(),
            Arg.Any<CancellationToken>());
        Assert.True(toastDelegateInvoked);
        Assert.True(serviceCalledWhileToastDelegateRunning);
    }

    [Fact]
    public async Task AnalyticsVM_RefreshForOpenAsync_ShowToastTrueWithCanceledToken_DoesNotInvokeDialogToastWrapper()
    {
        var service = CreateAnalyticsService();
        var dialogService = Substitute.For<IDialogService>();
        var uiSettleAwaiter = Substitute.For<IUiSettleAwaiter>();
        var vm = new AnalyticsVM(service, dialogService, uiSettleAwaiter);
        var gateField = typeof(AnalyticsVM).GetField("_refreshFeedbackGate", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(gateField);

        var refreshFeedbackGate = (SemaphoreSlim?)gateField!.GetValue(vm);
        Assert.NotNull(refreshFeedbackGate);

        await refreshFeedbackGate!.WaitAsync(CancellationToken.None);
        var cancellationSource = new CancellationTokenSource();

        try
        {
            var refreshTask = vm.RefreshForOpenAsync(showToast: true, cancellationSource.Token);
            await Task.Delay(50);

            refreshFeedbackGate.Release();
            cancellationSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await refreshTask);
        }
        finally
        {
            if (refreshFeedbackGate.CurrentCount == 0)
                refreshFeedbackGate.Release();
        }

        await dialogService.DidNotReceive().ShowToastWhileAsync(
            Arg.Any<string>(),
            Arg.Any<Func<Task>>(),
            Arg.Any<Window?>());
    }

    private static IAnalyticsService CreateAnalyticsService()
    {
        var service = Substitute.For<IAnalyticsService>();
        service.GetAnalyticsAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateAnalyticsDto()));

        return service;
    }

    private static AnalyticsDto CreateAnalyticsDto()
    {
        return new AnalyticsDto(
            TotalIncome: 1000m,
            TotalExpense: 500m,
            TimeSeries:
            [
                new AnalyticsTimeSeriesPoint(new DateOnly(2026, 1, 1), 100m, 40m),
                new AnalyticsTimeSeriesPoint(new DateOnly(2026, 1, 2), 160m, 60m),
                new AnalyticsTimeSeriesPoint(new DateOnly(2026, 1, 3), 120m, 55m)
            ],
            CategoryRatio:
            [
                new AnalyticsCategorySlice(ExpenseCategory.Needs, 200m),
                new AnalyticsCategorySlice(ExpenseCategory.Wants, 150m),
                new AnalyticsCategorySlice(ExpenseCategory.Savings, 100m)
            ],
            TopSpendingTags:
            [
                new AnalyticsTagTotal("Food", "#FFB86C", 100m)
            ],
            GoalsCreatedInPeriod: []);
    }

    private static AnalyticsVM CreateVm()
    {
        return new AnalyticsVM(CreateAnalyticsService());
    }
}
