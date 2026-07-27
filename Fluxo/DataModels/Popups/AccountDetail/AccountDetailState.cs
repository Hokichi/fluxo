using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.History;
using Fluxo.Services.Logging;
using Fluxo.Services.Notifications;
using Fluxo.Helpers.Popups;
using Fluxo.ViewModels.Shell;
using MainVM = Fluxo.ViewModels.Shell.Main.MainVM;

namespace Fluxo.DataModels.Popups.AccountDetail;

public readonly record struct AccountDetailState(
        string Name,
        AccountType AccountType,
        decimal PrimaryAmount,
        decimal AccountLimit,
        decimal MaximumSpending,
        decimal? MinimumPayment,
        decimal SpentAmount,
        int? MonthlyDueDate,
        int? DeductSource,
        decimal? InterestRate,
        bool IsEnabled,
        bool PinnedOnUI)
    {
        public static AccountDetailState Empty => new(
            string.Empty,
            AccountType.Checking,
            0m,
            0m,
            0m,
            null,
            0m,
            null,
            null,
            null,
            true,
            true);
    }
