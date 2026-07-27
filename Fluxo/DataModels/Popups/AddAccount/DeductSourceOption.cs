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

public readonly record struct DeductSourceOption(
        int Id,
        string Name,
        AccountType AccountType = AccountType.Checking)
    {
        public string TypeDisplayName => AccountType switch
        {
            AccountType.Credit => "Credit",
            AccountType.Checking => "Checking",
            AccountType.Cash => "Cash",
            AccountType.Saving => "Savings",
            _ => "Account"
        };
    }
