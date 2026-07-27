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

public readonly record struct AccountDetailResult(bool IsSuccess, bool ShouldClose, string? ErrorMessage)
    {
        public static AccountDetailResult Success(bool shouldClose = false)
        {
            return new AccountDetailResult(true, shouldClose, null);
        }

        public static AccountDetailResult Failure(string? errorMessage)
        {
            return new AccountDetailResult(false, false, errorMessage);
        }
    }
