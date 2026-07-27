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
using Fluxo.ViewModels.Popups;

namespace Fluxo.DataModels.Popups.AddAccount;

public readonly record struct AddAccountResult(
        bool IsSuccess,
        bool ShouldClose,
        string? ErrorMessage,
        AddAccountFailurePresentation FailurePresentation = AddAccountFailurePresentation.Dialog)
    {
        public static AddAccountResult Success(bool shouldClose = false)
        {
            return new AddAccountResult(true, shouldClose, null);
        }

        public static AddAccountResult Failure(
            string? errorMessage,
            AddAccountFailurePresentation failurePresentation = AddAccountFailurePresentation.Dialog)
        {
            return new AddAccountResult(false, false, errorMessage, failurePresentation);
        }
    }
