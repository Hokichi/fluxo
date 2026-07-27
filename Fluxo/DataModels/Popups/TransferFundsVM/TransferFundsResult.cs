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

namespace Fluxo.DataModels.Popups.TransferFundsVM;

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
