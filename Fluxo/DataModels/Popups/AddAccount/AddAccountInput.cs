using System.Globalization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.History;
using Fluxo.Services.Logging;
using Fluxo.Services.Notifications;
using Fluxo.ViewModels.Shell;
using MainVM = Fluxo.ViewModels.Shell.Main.MainVM;
using Fluxo.Helpers.Popups;

namespace Fluxo.DataModels.Popups.AddAccount;

public readonly record struct AddAccountInput(
        string Name,
        AccountType AccountType,
        decimal Balance,
        decimal SpentAmount,
        decimal AccountLimit,
        decimal MaximumSpending,
        decimal? MinimumPayment,
        int? MonthlyDueDate,
        int? DeductSource,
        decimal? InterestRate,
        bool PinnedOnUI,
        bool IsEnabled,
        bool IsDefault);
